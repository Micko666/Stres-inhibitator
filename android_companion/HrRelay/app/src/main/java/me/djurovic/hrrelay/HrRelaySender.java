package me.djurovic.hrrelay;

import android.util.Log;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.net.SocketException;
import java.net.SocketTimeoutException;
import java.nio.charset.StandardCharsets;
import java.util.List;

/**
 * Sends protocol-v1 samples/heartbeats to one endpoint and listens for
 * protocol-v1 ACK packets on the same UDP source port. A configured socket
 * means only "sending"; delivery is confirmed separately by a fresh ACK.
 *
 * <h3>Auto-discovery</h3>
 * When no address is configured the sender enters DISCOVERY: it broadcasts
 * heartbeat packets on the LAN and waits for an ACK. Unity/Quest already ACKs
 * any valid protocol-v1 packet (heartbeats included) back to its UDP source,
 * so the responder's address IS the endpoint — no new packet kind and no Quest
 * rebuild are required. The first valid ACK locks the target and discovery
 * stops. Because the ACK must carry this session's token, a stray headset on
 * the same network cannot capture the stream.
 *
 * Manual configuration always wins: {@link #setTarget} leaves discovery mode
 * immediately and is never overridden by a later broadcast reply.
 */
public final class HrRelaySender {

    public interface AckListener { void onAck(RelayAck ack); }

    /** Notified once when a broadcast probe is answered. */
    public interface DiscoveryListener { void onDiscovered(String host, int port); }

    private static final String TAG = "HrRelay/Sender";
    private static final int ACK_BUFFER_BYTES = 512;
    private static final int ACK_RECEIVE_TIMEOUT_MS = 1000;

    private final String sessionToken;
    private final AckListener ackListener;
    private volatile DiscoveryListener discoveryListener;
    private volatile InetAddress target;
    private volatile int port;
    private volatile DatagramSocket socket;
    private volatile boolean ackListening;
    private volatile boolean discovering;
    private Thread ackThread;
    private int sequence;

    public HrRelaySender(String sessionToken) {
        this(sessionToken, null);
    }

    public HrRelaySender(String sessionToken, AckListener ackListener) {
        this.sessionToken = sessionToken == null ? "" : sessionToken;
        this.ackListener = ackListener;
    }

    public void setDiscoveryListener(DiscoveryListener l) {
        this.discoveryListener = l;
    }

    /** (Re)point the sender at a Unity Editor/Quest endpoint. Ends discovery. */
    public synchronized void setTarget(String host, int port) throws Exception {
        InetAddress resolved = InetAddress.getByName(host);
        ensureSocketLocked();
        this.target = resolved;
        this.port = port;
        this.discovering = false;
    }

    /**
     * Enter discovery: no fixed target, broadcast probes are sent by
     * {@link #sendDiscoveryProbe()} until an ACK arrives.
     */
    public synchronized void startDiscovery(int port) throws Exception {
        ensureSocketLocked();
        this.target = null;
        this.port = port;
        this.discovering = true;
    }

    public boolean isDiscovering() {
        return discovering;
    }

    private void ensureSocketLocked() throws SocketException {
        if (socket == null || socket.isClosed()) {
            socket = new DatagramSocket();
            socket.setSoTimeout(ACK_RECEIVE_TIMEOUT_MS);
            try {
                socket.setBroadcast(true);
            } catch (Exception e) {
                Log.w(TAG, "Broadcast not permitted on this socket: " + e.getMessage());
            }
            startAckListenerLocked();
        }
    }

    public synchronized boolean isConfigured() {
        return target != null && socket != null && !socket.isClosed();
    }

    /** Returns the sequence used, or -1 if not configured / send failed. */
    public synchronized int sendSample(int bpm, long watchUnixSeconds) {
        String json = RelayPacketBuilder.sample(sessionToken, sequence, bpm, watchUnixSeconds);
        return send(json);
    }

    public synchronized int sendHeartbeat() {
        String json = RelayPacketBuilder.heartbeat(sessionToken, sequence);
        return send(json);
    }

    /**
     * Broadcasts one heartbeat to every LAN broadcast address. Harmless if the
     * Quest is absent; the packet is a transport heartbeat (bpm 0) and is never
     * treated as a physiological sample by the receiver.
     */
    public synchronized void sendDiscoveryProbe() {
        DatagramSocket activeSocket = socket;
        if (!discovering || activeSocket == null || activeSocket.isClosed()) return;

        String json = RelayPacketBuilder.heartbeat(sessionToken, sequence);
        byte[] bytes = json.getBytes(StandardCharsets.UTF_8);
        List<InetAddress> targets = LanBroadcast.targets();
        boolean sentAny = false;
        for (InetAddress addr : targets) {
            try {
                activeSocket.send(new DatagramPacket(bytes, bytes.length, addr, port));
                sentAny = true;
            } catch (Exception e) {
                Log.w(TAG, "Discovery probe to " + addr + " failed: " + e.getMessage());
            }
        }
        if (sentAny) sequence++;
    }

    private int send(String json) {
        DatagramSocket activeSocket = socket;
        InetAddress activeTarget = target;
        if (activeTarget == null || activeSocket == null || activeSocket.isClosed()) return -1;
        try {
            byte[] bytes = json.getBytes(StandardCharsets.UTF_8);
            activeSocket.send(new DatagramPacket(bytes, bytes.length, activeTarget, port));
            int used = sequence;
            sequence++;
            return used;
        } catch (Exception e) {
            Log.w(TAG, "UDP send failed: " + e.getMessage());
            return -1;
        }
    }

    private void startAckListenerLocked() {
        if (ackListening) return;
        ackListening = true;
        ackThread = new Thread(this::ackLoop, "HrRelayAck");
        ackThread.setDaemon(true);
        ackThread.start();
    }

    private void ackLoop() {
        byte[] buffer = new byte[ACK_BUFFER_BYTES];
        while (ackListening) {
            DatagramSocket activeSocket = socket;
            if (activeSocket == null || activeSocket.isClosed()) break;
            try {
                DatagramPacket packet = new DatagramPacket(buffer, buffer.length);
                activeSocket.receive(packet);

                RelayAck ack = RelayAckParser.parse(packet.getData(), packet.getLength());
                if (ack == null) continue;

                if (discovering) {
                    // The responder answered our broadcast: adopt it. The ACK's
                    // session token is validated downstream by RelayAckTracker,
                    // so a foreign headset cannot claim the stream.
                    adoptDiscovered(packet.getAddress());
                } else {
                    InetAddress expected = target;
                    if (expected != null && !expected.equals(packet.getAddress())) continue;
                }
                if (ackListener != null) ackListener.onAck(ack);
            } catch (SocketTimeoutException ignored) {
                // Periodic wake-up so close() can stop the thread promptly.
            } catch (SocketException e) {
                if (ackListening) Log.w(TAG, "ACK socket error: " + e.getMessage());
                break;
            } catch (Exception e) {
                Log.w(TAG, "ACK receive failed: " + e.getMessage());
            }
        }
        ackListening = false;
    }

    private void adoptDiscovered(InetAddress address) {
        if (address == null) return;
        DiscoveryListener listener;
        int adoptedPort;
        synchronized (this) {
            if (!discovering) return;
            target = address;
            discovering = false;
            adoptedPort = port;
            listener = discoveryListener;
        }
        Log.i(TAG, "Discovered endpoint: " + address.getHostAddress() + ":" + adoptedPort);
        if (listener != null) listener.onDiscovered(address.getHostAddress(), adoptedPort);
    }

    public synchronized int localPort() {
        return socket != null && !socket.isClosed() ? socket.getLocalPort() : -1;
    }

    public synchronized void close() {
        ackListening = false;
        discovering = false;
        DatagramSocket activeSocket = socket;
        socket = null;
        target = null;
        if (activeSocket != null) {
            try { activeSocket.close(); } catch (Exception ignore) {}
        }
        if (ackThread != null) {
            ackThread.interrupt();
            ackThread = null;
        }
    }
}
