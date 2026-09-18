package me.djurovic.hrrelay;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.os.Build;
import android.os.IBinder;
import android.os.PowerManager;
import android.util.Log;

/**
 * Foreground service that reads Mi Fitness HR logs and relays protocol-v1 UDP
 * packets to the configured Unity Editor or standalone Quest endpoint.
 * Configuring a DatagramSocket means only that packets can be sent. A fresh ACK
 * is required before the UI reports confirmed receipt.
 */
public final class HrRelayService extends Service implements LogcatHrReader.Listener,
        HrRelaySender.AckListener, HrRelaySender.DiscoveryListener {

    public static final String ACTION_START = "me.djurovic.hrrelay.START";
    public static final String ACTION_STOP = "me.djurovic.hrrelay.STOP";
    public static final String EXTRA_HOST = "quest_host";
    public static final String EXTRA_PORT = "quest_port";
    public static final String EXTRA_TOKEN = "session_token";
    public static final String EXTRA_TARGET_KIND = "target_kind";

    private static final String TAG = "HrRelay/Service";
    private static final String CHANNEL_ID = "hr_relay";
    private static final int NOTIF_ID = 4711;
    private static final long HEARTBEAT_MS = 5000;
    /** Probe cadence while searching — faster than the heartbeat so pairing feels instant. */
    private static final long DISCOVERY_PROBE_MS = 2000;
    /**
     * With no ACK for this long in automatic mode the endpoint is presumed gone
     * (Quest rebooted, new DHCP lease, different network) and discovery restarts.
     * Manual mode never re-enters discovery: an address typed by hand is a
     * deliberate choice and must not be silently replaced.
     */
    private static final double ACK_LOST_SECONDS = 15.0;

    private final HrRelayState state = HrRelayState.get();
    private LogcatHrReader reader;
    private HrRelaySender sender;
    private PowerManager.WakeLock wakeLock;
    private Thread heartbeatThread;
    private volatile boolean running;
    /** true = address discovered automatically; false = typed by the user. */
    private volatile boolean autoMode;
    private volatile int targetPort = 5005;
    private volatile String targetKindLabel = "Unity";

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent != null && ACTION_STOP.equals(intent.getAction())) {
            stopEverything();
            stopSelf();
            return START_NOT_STICKY;
        }

        String host = intent != null ? intent.getStringExtra(EXTRA_HOST) : null;
        int port = intent != null ? intent.getIntExtra(EXTRA_PORT, 5005) : 5005;
        String token = intent != null ? intent.getStringExtra(EXTRA_TOKEN) : "";
        String targetKind = intent != null ? intent.getStringExtra(EXTRA_TARGET_KIND) : "Unity";

        ensureChannel();
        startForeground(NOTIF_ID, buildNotification("HR relej aktivan", "Priprema slanja…"));
        acquireWakeLock();

        if (running) {
            reconfigureTarget(host, port, targetKind);
            return START_STICKY;
        }
        running = true;
        state.serviceRunning = true;
        state.configureSessionToken(token);
        state.errorText = "";

        sender = new HrRelaySender(token, this);
        reconfigureTarget(host, port, targetKind);

        reader = new LogcatHrReader(this);
        reader.start();

        startHeartbeat();
        state.notifyChanged();
        return START_STICKY;
    }

    private void reconfigureTarget(String host, int port, String targetKind) {
        targetPort = port;
        targetKindLabel = "Quest".equalsIgnoreCase(targetKind) ? "Quest" : "Unity";
        sender.setDiscoveryListener(this);

        // Empty address is not an error any more: it selects automatic discovery.
        if (host == null || host.trim().isEmpty()) {
            autoMode = true;
            beginDiscovery();
            return;
        }

        autoMode = false;
        try {
            sender.setTarget(host.trim(), port);
            state.onTargetConfigured(host.trim() + ":" + port, sender.localPort(), targetKindLabel);
            state.autoDiscovering = false;
            state.errorText = "";
        } catch (Exception e) {
            state.onTargetCleared();
            state.questTarget = host + ":" + port + " (neispravno)";
            state.errorText = "Neispravna adresa odredišta: " + e.getMessage();
        }
        state.notifyChanged();
        updateNotification();
    }

    /** Enter/re-enter automatic search for the receiver on the local network. */
    private void beginDiscovery() {
        try {
            sender.startDiscovery(targetPort);
            state.onTargetCleared();
            state.autoDiscovering = true;
            state.questTarget = "traži se…";
            state.errorText = "";
        } catch (Exception e) {
            state.autoDiscovering = false;
            state.errorText = "Traženje nije moglo da počne: " + e.getMessage();
        }
        state.notifyChanged();
        updateNotification();
    }

    @Override
    public void onDiscovered(String host, int port) {
        state.onTargetConfigured(host + ":" + port, sender.localPort(), targetKindLabel);
        state.autoDiscovering = false;
        state.errorText = "";
        state.notifyChanged();
        updateNotification();
    }

    @Override
    public void onSample(HrSample sample) {
        state.onSample(sample.bpm);
        if (sender != null && sender.isConfigured()) {
            int seq = sender.sendSample(sample.bpm, sample.watchUnixSeconds);
            if (seq >= 0) state.onSent(seq);
        }
        updateNotification();
    }

    @Override
    public void onReaderState(boolean logcatAlive, String detail) {
        state.logcatAlive = logcatAlive;
        state.readerDetail = detail;
        state.notifyChanged();
        updateNotification();
    }

    @Override
    public void onAck(RelayAck ack) {
        if (state.onAck(ack)) updateNotification();
    }

    /**
     * True when the endpoint has stopped acknowledging. A negative age means no
     * ACK has ever arrived, which in automatic mode cannot happen (the target is
     * only adopted because it ACKed), so it is not treated as a loss.
     */
    private boolean ackLost() {
        double age = state.lastAckAgeSeconds();
        return age >= 0 && age > ACK_LOST_SECONDS;
    }

    private void startHeartbeat() {
        heartbeatThread = new Thread(() -> {
            while (running) {
                HrRelaySender s = sender;
                boolean searching = s != null && s.isDiscovering();
                long wait = searching ? DISCOVERY_PROBE_MS : HEARTBEAT_MS;
                try { Thread.sleep(wait); } catch (InterruptedException e) { return; }

                s = sender;
                if (s == null) continue;

                if (s.isDiscovering()) {
                    s.sendDiscoveryProbe();      // broadcast until someone ACKs
                    continue;
                }
                if (s.isConfigured()) {
                    int seq = s.sendHeartbeat();
                    if (seq >= 0) state.onSent(seq);
                    // Endpoint went quiet: in automatic mode start looking again
                    // so a rebooted Quest or a new DHCP lease reconnects itself.
                    if (autoMode && ackLost()) beginDiscovery();
                }
            }
        }, "HrRelayHeartbeat");
        heartbeatThread.setDaemon(true);
        heartbeatThread.start();
    }

    private void stopEverything() {
        running = false;
        state.serviceRunning = false;
        if (reader != null) { reader.stop(); reader = null; }
        if (heartbeatThread != null) { heartbeatThread.interrupt(); heartbeatThread = null; }
        if (sender != null) { sender.close(); sender = null; }
        releaseWakeLock();
        state.logcatAlive = false;
        state.readerDetail = "Zaustavljeno.";
        state.onTargetCleared();
        state.notifyChanged();
        stopForeground(true);
    }

    @Override
    public void onDestroy() {
        stopEverything();
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent intent) { return null; }

    private void acquireWakeLock() {
        if (wakeLock != null) return;
        PowerManager pm = (PowerManager) getSystemService(Context.POWER_SERVICE);
        wakeLock = pm.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "HrRelay:cpu");
        wakeLock.setReferenceCounted(false);
        wakeLock.acquire();
    }

    private void releaseWakeLock() {
        if (wakeLock != null && wakeLock.isHeld()) { try { wakeLock.release(); } catch (Exception ignore) {} }
        wakeLock = null;
    }

    private void ensureChannel() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return;
        NotificationManager nm = (NotificationManager) getSystemService(NOTIFICATION_SERVICE);
        if (nm.getNotificationChannel(CHANNEL_ID) == null) {
            NotificationChannel ch = new NotificationChannel(CHANNEL_ID, "HR relej",
                    NotificationManager.IMPORTANCE_LOW);
            ch.setDescription("Prosljeđivanje pulsa u Unity/Quest");
            nm.createNotificationChannel(ch);
        }
    }

    private Notification buildNotification(String title, String text) {
        Notification.Builder b = Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
                ? new Notification.Builder(this, CHANNEL_ID)
                : new Notification.Builder(this);
        return b.setContentTitle(title)
                .setContentText(text)
                .setSmallIcon(android.R.drawable.ic_menu_compass)
                .setOngoing(true)
                .build();
    }

    private void updateNotification() {
        String source = state.lastBpm > 0 ? state.lastBpm + " bpm" : "čekam stvarni puls";
        String delivery;
        if (!state.questConfigured) delivery = "odredište nije postavljeno";
        else if (state.isAckFresh()) delivery = state.destinationName + " potvrđen";
        else delivery = "šalje na " + state.questTarget + ", bez potvrde";
        try {
            NotificationManager nm = (NotificationManager) getSystemService(NOTIFICATION_SERVICE);
            nm.notify(NOTIF_ID, buildNotification("HR relej aktivan", source + " · " + delivery));
        } catch (Exception e) {
            Log.w(TAG, "notif update failed: " + e.getMessage());
        }
    }
}
