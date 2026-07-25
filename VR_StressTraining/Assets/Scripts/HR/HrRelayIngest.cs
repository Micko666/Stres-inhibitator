using System;
using StressTraining.Core;

namespace StressTraining.HR
{
    /// <summary>
    /// Pure, deterministic gate applied to every parsed UDP packet before it may
    /// become a heart-rate sample (spec §8/§9). It enforces, in order:
    ///
    ///   1. session identity  — binds to the FIRST phone-relay token seen and
    ///                           rejects any packet carrying a different token
    ///                           (a second phone / stray Quest cannot inject);
    ///   2. sequence ordering  — rejects duplicates and out-of-order packets;
    ///                           a sequence of 0 is treated as a legitimate sender
    ///                           restart and re-baselines instead of locking out;
    ///   3. heartbeat split    — heartbeats keep the transport-alive marker fresh
    ///                           but are NEVER emitted as physiological samples;
    ///   4. watch-time ordering — a sample whose watch timestamp is not newer than
    ///                           the newest accepted one is rejected (stale replay).
    ///
    /// No Unity dependency — unit tested directly. One instance per source; call
    /// <see cref="Reset"/> when the source (re)starts.
    /// </summary>
    public sealed class HrRelayIngest
    {
        public enum Decision { AcceptSample, Heartbeat, Reject }

        // A sequence this far below the last accepted one is read as a restart,
        // not an out-of-order straggler.
        private const int RestartSequenceGap = 100;

        private string _boundToken = "";
        private int _lastSequence = -1;
        private long _highestWatchUnixMs = long.MinValue;

        public string BoundToken => _boundToken;
        public int RejectedCount { get; private set; }
        public int DuplicateOrOutOfOrderCount { get; private set; }
        public int WrongTokenCount { get; private set; }
        public int StaleWatchCount { get; private set; }
        public int HeartbeatCount { get; private set; }

        public void Reset()
        {
            _boundToken = "";
            _lastSequence = -1;
            _highestWatchUnixMs = long.MinValue;
            RejectedCount = 0;
            DuplicateOrOutOfOrderCount = 0;
            WrongTokenCount = 0;
            StaleWatchCount = 0;
            HeartbeatCount = 0;
        }

        /// <summary>Force a specific expected token (optional explicit pairing).</summary>
        public void BindExpectedToken(string token)
        {
            _boundToken = token ?? "";
        }

        public Decision Evaluate(in HrPacketParser.Result parsed, out string reason)
        {
            reason = "";
            if (!parsed.Ok)
            {
                reason = "packet not ok";
                RejectedCount++;
                return Decision.Reject;
            }

            // 1. Session identity (phone-relay packets carry a token; the Python
            //    bridge and legacy packets carry none and skip this check).
            bool hasToken = !string.IsNullOrEmpty(parsed.SessionToken);
            if (hasToken)
            {
                if (string.IsNullOrEmpty(_boundToken))
                    _boundToken = parsed.SessionToken;
                else if (!string.Equals(_boundToken, parsed.SessionToken, StringComparison.Ordinal))
                {
                    reason = "wrong session token";
                    WrongTokenCount++;
                    RejectedCount++;
                    return Decision.Reject;
                }
            }

            // 2. Sequence ordering (skip when sequence is absent, i.e. -1).
            if (parsed.Sequence >= 0)
            {
                bool restart = parsed.Sequence == 0 ||
                               parsed.Sequence <= _lastSequence - RestartSequenceGap;
                if (!restart && parsed.Sequence <= _lastSequence)
                {
                    reason = "duplicate or out-of-order sequence";
                    DuplicateOrOutOfOrderCount++;
                    RejectedCount++;
                    return Decision.Reject;
                }
                if (restart)
                {
                    // Sender restarted: re-baseline sequence AND watch-time so a
                    // fresh session is not permanently locked out.
                    _lastSequence = parsed.Sequence;
                    _highestWatchUnixMs = long.MinValue;
                }
                else
                {
                    _lastSequence = parsed.Sequence;
                }
            }

            // 3. Heartbeat: transport alive, not a sample.
            if (parsed.IsHeartbeat)
            {
                HeartbeatCount++;
                reason = "heartbeat";
                return Decision.Heartbeat;
            }

            // 4. Watch-timestamp monotonicity (only when a timestamp is present).
            if (UtcTime.TryParseIso(parsed.SourceTimestampUtcIso, out var srcUtc))
            {
                long watchMs = (long)(srcUtc - UnixEpoch).TotalMilliseconds;
                if (watchMs <= _highestWatchUnixMs)
                {
                    reason = "stale watch timestamp";
                    StaleWatchCount++;
                    RejectedCount++;
                    return Decision.Reject;
                }
                _highestWatchUnixMs = watchMs;
            }

            return Decision.AcceptSample;
        }

        private static readonly DateTime UnixEpoch =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
