using System;

namespace StressTraining.Data
{
    /// <summary>
    /// One application event, appended as a single JSON line to events.jsonl.
    /// Every important decision (state transitions, pause open/close, termination,
    /// scheduler runs, interval bypasses, errors) is reconstructable from this stream.
    /// </summary>
    [Serializable]
    public sealed class EventRecord
    {
        public int schemaVersion = 1;

        public long eventId;                    // monotonically increasing per session
        public string eventType;                // e.g. "state_transition", "pause_opened", "error"
        public string timestampUtcIso;
        public double monotonicSeconds;         // SessionClock.RealElapsedSeconds (0 outside sessions)
        public string sessionId = "";
        public string appState = "";
        public string payloadJson = "";         // event-specific JSON payload
    }
}
