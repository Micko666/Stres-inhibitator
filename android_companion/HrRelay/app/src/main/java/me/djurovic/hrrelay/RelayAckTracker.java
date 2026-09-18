package me.djurovic.hrrelay;

import java.util.ArrayDeque;
import java.util.Deque;

/**
 * Validates ACK identity and freshness without depending on Android classes.
 * An ACK confirms delivery only when its token matches the active relay session
 * and its sequence is one of the packets actually sent by this process.
 */
public final class RelayAckTracker {
    public static final long DEFAULT_STALE_MS = 12_000L;
    private static final int RECENT_SEQUENCE_LIMIT = 32;

    private final Deque<Integer> recentSent = new ArrayDeque<>();
    private String sessionToken = "";
    private long lastAcceptedAckUptimeMs = -1;
    private int lastAcceptedAckSequence = -1;
    private int rejectedWrongToken;
    private int rejectedUnknownSequence;
    private int rejectedStatus;

    public synchronized void reset(String token) {
        sessionToken = token == null ? "" : token;
        recentSent.clear();
        lastAcceptedAckUptimeMs = -1;
        lastAcceptedAckSequence = -1;
        rejectedWrongToken = 0;
        rejectedUnknownSequence = 0;
        rejectedStatus = 0;
    }

    public synchronized void onSent(int sequence) {
        if (sequence < 0) return;
        recentSent.remove(sequence);
        recentSent.addLast(sequence);
        while (recentSent.size() > RECENT_SEQUENCE_LIMIT) recentSent.removeFirst();
    }

    public synchronized boolean accept(RelayAck ack, long nowUptimeMs) {
        if (ack == null || ack.protocolVersion != RelayPacketBuilder.PROTOCOL_VERSION ||
                !RelayAckParser.STATUS_ACCEPTED.equalsIgnoreCase(ack.status)) {
            rejectedStatus++;
            return false;
        }
        if (!sessionToken.equals(ack.sessionToken)) {
            rejectedWrongToken++;
            return false;
        }
        if (!recentSent.contains(ack.sequence)) {
            rejectedUnknownSequence++;
            return false;
        }
        lastAcceptedAckSequence = ack.sequence;
        lastAcceptedAckUptimeMs = nowUptimeMs;
        return true;
    }

    public synchronized boolean isFresh(long nowUptimeMs) {
        return lastAcceptedAckUptimeMs >= 0 &&
                nowUptimeMs - lastAcceptedAckUptimeMs <= DEFAULT_STALE_MS;
    }

    public synchronized double ageSeconds(long nowUptimeMs) {
        if (lastAcceptedAckUptimeMs < 0) return -1;
        return Math.max(0, nowUptimeMs - lastAcceptedAckUptimeMs) / 1000.0;
    }

    public synchronized int lastAcceptedSequence() { return lastAcceptedAckSequence; }
    public synchronized int rejectedWrongToken() { return rejectedWrongToken; }
    public synchronized int rejectedUnknownSequence() { return rejectedUnknownSequence; }
    public synchronized int rejectedStatus() { return rejectedStatus; }
}
