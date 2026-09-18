using System;
using UnityEngine;

namespace StressTraining.HR
{
    /// <summary>
    /// Protocol-v1 acknowledgement returned to the UDP source endpoint after a
    /// phone-relay packet passes parsing and ingest validation. An ACK confirms
    /// receipt by the active Unity process; it does not claim that the phone,
    /// watch or physiological signal is otherwise healthy.
    /// </summary>
    public static class HrRelayAckProtocol
    {
        public const int CurrentProtocolVersion = HrPacketParser.CurrentProtocolVersion;
        public const string StatusAccepted = "accepted";

        [Serializable]
        private sealed class AckPacket
        {
            public int protocolVersion;
            public string sessionToken;
            public int sequence;
            public string status;
        }

        public struct Result
        {
            public bool Ok;
            public int ProtocolVersion;
            public string SessionToken;
            public int Sequence;
            public string Status;
            public string Error;
        }

        public static bool ShouldSendAcceptedAck(in HrPacketParser.Result parsed,
            HrRelayIngest.Decision decision)
        {
            if (!parsed.Ok || decision == HrRelayIngest.Decision.Reject)
                return false;
            if (!string.Equals(parsed.Source, HrPacketParser.PhoneRelaySource,
                    StringComparison.OrdinalIgnoreCase))
                return false;
            return parsed.ProtocolVersion == CurrentProtocolVersion &&
                   parsed.Sequence >= 0 &&
                   !string.IsNullOrWhiteSpace(parsed.SessionToken);
        }

        public static string BuildAccepted(string sessionToken, int sequence)
        {
            if (string.IsNullOrWhiteSpace(sessionToken))
                throw new ArgumentException("ACK session token is required.", nameof(sessionToken));
            if (sequence < 0)
                throw new ArgumentOutOfRangeException(nameof(sequence));

            return JsonUtility.ToJson(new AckPacket
            {
                protocolVersion = CurrentProtocolVersion,
                sessionToken = sessionToken,
                sequence = sequence,
                status = StatusAccepted
            });
        }

        public static Result Parse(string json)
        {
            var result = new Result
            {
                Sequence = -1,
                SessionToken = "",
                Status = "",
                Error = ""
            };
            if (string.IsNullOrWhiteSpace(json))
            {
                result.Error = "empty ACK";
                return result;
            }

            try
            {
                var packet = JsonUtility.FromJson<AckPacket>(json);
                if (packet == null)
                {
                    result.Error = "malformed ACK";
                    return result;
                }
                if (packet.protocolVersion != CurrentProtocolVersion)
                {
                    result.Error = "unsupported ACK protocol version";
                    return result;
                }
                if (string.IsNullOrWhiteSpace(packet.sessionToken))
                {
                    result.Error = "ACK session token missing";
                    return result;
                }
                if (packet.sequence < 0)
                {
                    result.Error = "ACK sequence missing";
                    return result;
                }
                if (!string.Equals(packet.status, StatusAccepted, StringComparison.OrdinalIgnoreCase))
                {
                    result.Error = "ACK status not accepted";
                    return result;
                }

                result.Ok = true;
                result.ProtocolVersion = packet.protocolVersion;
                result.SessionToken = packet.sessionToken;
                result.Sequence = packet.sequence;
                result.Status = packet.status;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = "ACK parse exception: " + ex.Message;
                return result;
            }
        }
    }
}
