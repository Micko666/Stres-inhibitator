package me.djurovic.hrrelay;

import android.os.SystemClock;

/**
 * Process-wide observable state shared between the service and UI. No HR history
 * is retained. Reading logcat, receiving a fresh HR sample, sending UDP packets
 * and receiving a fresh Unity ACK are four separate states.
 */
public final class HrRelayState {

    public interface Listener { void onStateChanged(); }

    public static final long HR_SAMPLE_FRESH_MS = 10_000L;

    private static final HrRelayState INSTANCE = new HrRelayState();
    public static HrRelayState get() { return INSTANCE; }
    private HrRelayState() {}

    private volatile Listener listener;
    private final RelayAckTracker ackTracker = new RelayAckTracker();

    // Band / HR source
    public volatile boolean logcatAlive;
    public volatile String readerDetail = "Nije pokrenuto.";
    public volatile int lastBpm = -1;
    private volatile long lastSampleUptimeMs = -1;

    // UDP destination / send state
    public volatile boolean questConfigured;
    public volatile String questTarget = "-";
    public volatile String destinationName = "Unity/Quest";
    public volatile int senderLocalPort = -1;
    public volatile int lastSequenceSent = -1;
    private volatile long lastSendUptimeMs = -1;

    // ACK state
    public volatile int lastAckSequence = -1;

    // Service / errors
    public volatile boolean serviceRunning;
    public volatile String errorText = "";
    public volatile String sessionToken = "";

    public void setListener(Listener l) { listener = l; }

    public synchronized void configureSessionToken(String token) {
        sessionToken = token == null ? "" : token;
        ackTracker.reset(sessionToken);
        lastAckSequence = -1;
        notifyChanged();
    }

    public synchronized void onTargetConfigured(String target, int localPort, String displayName) {
        questConfigured = true;
        questTarget = target == null ? "-" : target;
        destinationName = displayName == null || displayName.trim().isEmpty()
                ? "Unity/Quest" : displayName.trim();
        senderLocalPort = localPort;
        ackTracker.reset(sessionToken);
        lastAckSequence = -1;
        notifyChanged();
    }

    public synchronized void onTargetCleared() {
        questConfigured = false;
        questTarget = "-";
        destinationName = "Unity/Quest";
        senderLocalPort = -1;
        ackTracker.reset(sessionToken);
        lastAckSequence = -1;
        notifyChanged();
    }

    public void onSample(int bpm) {
        lastBpm = bpm;
        lastSampleUptimeMs = SystemClock.elapsedRealtime();
        notifyChanged();
    }

    public synchronized void onSent(int sequence) {
        lastSequenceSent = sequence;
        lastSendUptimeMs = SystemClock.elapsedRealtime();
        ackTracker.onSent(sequence);
        notifyChanged();
    }

    public synchronized boolean onAck(RelayAck ack) {
        boolean accepted = ackTracker.accept(ack, SystemClock.elapsedRealtime());
        if (accepted) lastAckSequence = ack.sequence;
        notifyChanged();
        return accepted;
    }

    public double lastSampleAgeSeconds() {
        if (lastSampleUptimeMs < 0) return -1;
        return (SystemClock.elapsedRealtime() - lastSampleUptimeMs) / 1000.0;
    }

    public boolean isHrSampleFresh() {
        return lastSampleUptimeMs >= 0 &&
                SystemClock.elapsedRealtime() - lastSampleUptimeMs <= HR_SAMPLE_FRESH_MS;
    }

    public double lastSendAgeSeconds() {
        if (lastSendUptimeMs < 0) return -1;
        return (SystemClock.elapsedRealtime() - lastSendUptimeMs) / 1000.0;
    }

    public boolean isAckFresh() {
        return ackTracker.isFresh(SystemClock.elapsedRealtime());
    }

    public double lastAckAgeSeconds() {
        return ackTracker.ageSeconds(SystemClock.elapsedRealtime());
    }

    public int ackWrongTokenCount() { return ackTracker.rejectedWrongToken(); }
    public int ackWrongSequenceCount() { return ackTracker.rejectedUnknownSequence(); }
    public int ackRejectedStatusCount() { return ackTracker.rejectedStatus(); }

    public void notifyChanged() {
        Listener l = listener;
        if (l != null) l.onStateChanged();
    }
}
