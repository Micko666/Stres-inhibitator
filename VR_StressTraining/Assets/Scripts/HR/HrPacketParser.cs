using System;
using StressTraining.Core;
using UnityEngine;

namespace StressTraining.HR
{
    /// <summary>
    /// Versioned UDP parser. Protocol v1 accepts the existing Python bridge
    /// schema and the future Android phone-relay envelope. Phone-relay packets
    /// are rejected when their session token is missing; token pairing itself
    /// remains a future spike.
    /// </summary>
    public static class HrPacketParser
    {
        public const int CurrentProtocolVersion = 1;
        public const string PhoneRelaySource = "phone-relay";

        public const string KindHeartbeat = "heartbeat";
        public const string KindSample = "sample";

        public struct Result
        {
            public bool Ok;
            public int Bpm;
            public int Sequence;
            public string SourceTimestampUtcIso;
            public string Source;
            public bool IsLegacy;
            public int ProtocolVersion;
            public string SessionToken;
            public string Kind;          // "sample" | "heartbeat" (relay only)
            public bool IsHeartbeat;     // transport-alive marker, NOT an HR sample
            public string Error;
        }

        [Serializable]
        private sealed class BridgePacket
        {
            public int schemaVersion;
            public int sequence = -1;
            public int hr = -1;
            public string sourceTimestampUtc = "";
            public string sentAtUtc = "";
            public string source = "";
        }

        [Serializable]
        private sealed class RelayPacket
        {
            public int protocolVersion;
            public string sessionToken = "";
            public int sequence = -1;
            public string timestampUtc = "";
            public int bpm = -1;
            public string source = "";
            public string kind = "";     // "" defaults to sample (older senders)
        }

        [Serializable]
        private sealed class LegacyPacket
        {
            public int hr = -1;
            public string ts = "";
        }

        public static Result Parse(string json, int minPlausibleBpm = 30, int maxPlausibleBpm = 220)
        {
            var result = new Result
            {
                Sequence = -1,
                SourceTimestampUtcIso = "",
                Source = "",
                SessionToken = "",
                Error = ""
            };
            if (string.IsNullOrWhiteSpace(json))
            {
                result.Error = "empty payload";
                return result;
            }

            try
            {
                if (json.Contains("\"protocolVersion\""))
                {
                    var packet = JsonUtility.FromJson<RelayPacket>(json);
                    if (packet == null)
                    { result.Error = "malformed relay packet"; return result; }
                    if (packet.protocolVersion != CurrentProtocolVersion)
                    { result.Error = "unsupported protocol version"; return result; }

                    bool isHeartbeat = string.Equals(packet.kind, KindHeartbeat,
                        StringComparison.OrdinalIgnoreCase);
                    // A phone-relay packet ALWAYS needs its session token (identity),
                    // heartbeat or sample — an unpaired sender is rejected outright.
                    if (string.Equals(packet.source, PhoneRelaySource, StringComparison.OrdinalIgnoreCase) &&
                        string.IsNullOrWhiteSpace(packet.sessionToken))
                    { result.Error = "phone-relay session token missing"; return result; }

                    if (isHeartbeat)
                    {
                        // Transport-alive marker: carries no HR, must not be gated
                        // on bpm plausibility, and is never emitted as a sample.
                        result.Ok = true;
                        result.IsHeartbeat = true;
                        result.Kind = KindHeartbeat;
                        result.Bpm = 0;
                        result.Sequence = packet.sequence;
                        result.SourceTimestampUtcIso = packet.timestampUtc ?? "";
                        result.Source = packet.source ?? "";
                        result.SessionToken = packet.sessionToken ?? "";
                        result.ProtocolVersion = packet.protocolVersion;
                        return result;
                    }

                    if (packet.bpm < 0)
                    { result.Error = "malformed relay packet"; return result; }
                    if (!Plausible(packet.bpm, minPlausibleBpm, maxPlausibleBpm, ref result))
                        return result;

                    result.Ok = true;
                    result.Kind = KindSample;
                    result.Bpm = packet.bpm;
                    result.Sequence = packet.sequence;
                    result.SourceTimestampUtcIso = packet.timestampUtc ?? "";
                    result.Source = packet.source ?? "";
                    result.SessionToken = packet.sessionToken ?? "";
                    result.ProtocolVersion = packet.protocolVersion;
                    return result;
                }

                if (json.Contains("\"schemaVersion\""))
                {
                    var packet = JsonUtility.FromJson<BridgePacket>(json);
                    if (packet == null || packet.hr < 0)
                    { result.Error = "malformed bridge packet"; return result; }
                    if (packet.schemaVersion < 1)
                    { result.Error = "unsupported bridge schema"; return result; }
                    if (!Plausible(packet.hr, minPlausibleBpm, maxPlausibleBpm, ref result))
                        return result;

                    result.Ok = true;
                    result.Bpm = packet.hr;
                    result.Sequence = packet.sequence;
                    result.SourceTimestampUtcIso = packet.sourceTimestampUtc ?? "";
                    result.Source = packet.source ?? "";
                    result.ProtocolVersion = CurrentProtocolVersion;
                    return result;
                }

                var legacy = JsonUtility.FromJson<LegacyPacket>(json);
                if (legacy == null || legacy.hr < 0)
                { result.Error = "malformed legacy packet"; return result; }
                if (!Plausible(legacy.hr, minPlausibleBpm, maxPlausibleBpm, ref result))
                    return result;
                result.Ok = true;
                result.Bpm = legacy.hr;
                result.IsLegacy = true;
                result.ProtocolVersion = 0;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = "parse exception: " + ex.Message;
                return result;
            }
        }

        private static bool Plausible(int bpm, int min, int max, ref Result result)
        {
            if (bpm >= min && bpm <= max) return true;
            result.Error = $"bpm {bpm} out of plausible range";
            return false;
        }
    }
}
