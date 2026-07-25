package me.djurovic.hrrelay;

import org.junit.Test;

import static org.junit.Assert.*;

public final class RelayAckTrackerTest {

    @Test
    public void validKnownSequenceConfirmsCurrentFlow() {
        RelayAckTracker tracker = new RelayAckTracker();
        tracker.reset("token-a");
        tracker.onSent(4);
        RelayAck ack = RelayAckParser.parse(
                "{\"protocolVersion\":1,\"sessionToken\":\"token-a\"," +
                "\"sequence\":4,\"status\":\"accepted\"}");
        assertNotNull(ack);
        assertTrue(tracker.accept(ack, 1000));
        assertTrue(tracker.isFresh(1000));
    }

    @Test
    public void wrongTokenAckIsDiscarded() {
        RelayAckTracker tracker = new RelayAckTracker();
        tracker.reset("token-a");
        tracker.onSent(4);
        RelayAck ack = RelayAckParser.parse(
                "{\"protocolVersion\":1,\"sessionToken\":\"token-b\"," +
                "\"sequence\":4,\"status\":\"accepted\"}");
        assertFalse(tracker.accept(ack, 1000));
        assertFalse(tracker.isFresh(1000));
        assertEquals(1, tracker.rejectedWrongToken());
    }

    @Test
    public void unknownSequenceDoesNotConfirmCurrentFlow() {
        RelayAckTracker tracker = new RelayAckTracker();
        tracker.reset("token-a");
        tracker.onSent(4);
        RelayAck ack = RelayAckParser.parse(
                "{\"protocolVersion\":1,\"sessionToken\":\"token-a\"," +
                "\"sequence\":999,\"status\":\"accepted\"}");
        assertFalse(tracker.accept(ack, 1000));
        assertFalse(tracker.isFresh(1000));
        assertEquals(1, tracker.rejectedUnknownSequence());
    }

    @Test
    public void staleAckChangesStateToNoConfirmation() {
        RelayAckTracker tracker = new RelayAckTracker();
        tracker.reset("token-a");
        tracker.onSent(4);
        RelayAck ack = new RelayAck(1, "token-a", 4, "accepted");
        assertTrue(tracker.accept(ack, 1000));
        assertTrue(tracker.isFresh(1000 + RelayAckTracker.DEFAULT_STALE_MS));
        assertFalse(tracker.isFresh(1001 + RelayAckTracker.DEFAULT_STALE_MS));
        assertEquals(RelayStatusText.NO_CONFIRMATION,
                RelayStatusText.confirmation(true,
                        tracker.isFresh(1001 + RelayAckTracker.DEFAULT_STALE_MS), "Quest"));
    }
}
