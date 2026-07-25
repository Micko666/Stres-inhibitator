using System.Collections.Generic;
using StressTraining.Data;

namespace StressTraining.HR
{
    /// <summary>
    /// Raw sample as delivered by a source, before session-context enrichment.
    /// Network payload strings are parsed on the main thread (HrPacketParser);
    /// sources only queue.
    /// </summary>
    public struct RawHrSample
    {
        public int Bpm;
        public string ReceivedAtUtcIso;
        public double RealtimeAtReceive;        // Time.realtimeSinceStartup at enqueue
        public string SourceTimestampUtcIso;    // "" when unknown (legacy packets)
        public int Sequence;                    // -1 for legacy packets
        public bool IsLegacyPacket;
        public double SignalAgeMs;              // -1 when source timestamp unknown
        public int ProtocolVersion;              // 0 legacy, 1 current bridge/relay
        public string Source;                    // transport producer identifier
        public bool HasSessionToken;             // required for source=phone-relay
    }

    /// <summary>
    /// A heart-rate source produces raw samples. The HR source NEVER interprets
    /// stress (architecture rule 6) — zones/aggregation live in HeartRateService
    /// and HeartRateZoneEvaluator.
    /// </summary>
    public interface IHeartRateSource
    {
        HrSourceType SourceType { get; }
        bool IsRunning { get; }
        void StartSource();
        void StopSource();
        /// <summary>Main-thread pump. Appends pending samples to the buffer, returns count.</summary>
        int DrainSamples(List<RawHrSample> buffer);
    }
}
