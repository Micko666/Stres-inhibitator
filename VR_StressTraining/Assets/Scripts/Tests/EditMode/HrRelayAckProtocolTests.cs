using System.IO;
using NUnit.Framework;
using StressTraining.HR;
using UnityEngine;

namespace StressTraining.Tests.EditMode
{
    public sealed class HrRelayAckProtocolTests
    {
        private static HrPacketParser.Result ParseRelay(string token, int sequence,
            string kind, int bpm = 72)
        {
            string json = "{\"protocolVersion\":1,\"sessionToken\":\"" + token +
                          "\",\"sequence\":" + sequence +
                          ",\"timestampUtc\":\"2026-07-14T08:00:00Z\",\"bpm\":" + bpm +
                          ",\"source\":\"phone-relay\",\"kind\":\"" + kind + "\"}";
            return HrPacketParser.Parse(json);
        }

        [Test]
        public void ValidPhoneRelaySampleProducesAcceptedAck()
        {
            var parsed = ParseRelay("token-a", 12, HrPacketParser.KindSample);
            var ingest = new HrRelayIngest();
            var decision = ingest.Evaluate(in parsed, out _);

            Assert.That(decision, Is.EqualTo(HrRelayIngest.Decision.AcceptSample));
            Assert.That(HrRelayAckProtocol.ShouldSendAcceptedAck(in parsed, decision), Is.True);

            var ack = HrRelayAckProtocol.Parse(
                HrRelayAckProtocol.BuildAccepted(parsed.SessionToken, parsed.Sequence));
            Assert.That(ack.Ok, Is.True);
            Assert.That(ack.SessionToken, Is.EqualTo("token-a"));
            Assert.That(ack.Sequence, Is.EqualTo(12));
            Assert.That(ack.Status, Is.EqualTo(HrRelayAckProtocol.StatusAccepted));
        }

        [Test]
        public void HeartbeatMayReceiveAckButNeverBecomesHrSample()
        {
            var parsed = ParseRelay("token-a", 13, HrPacketParser.KindHeartbeat, 0);
            var ingest = new HrRelayIngest();
            var decision = ingest.Evaluate(in parsed, out _);

            Assert.That(parsed.IsHeartbeat, Is.True);
            Assert.That(decision, Is.EqualTo(HrRelayIngest.Decision.Heartbeat));
            Assert.That(HrRelayAckProtocol.ShouldSendAcceptedAck(in parsed, decision), Is.True);
            Assert.That(parsed.Bpm, Is.Zero);
        }

        [Test]
        public void WrongTokenDoesNotProduceAcceptedAck()
        {
            var ingest = new HrRelayIngest();
            var first = ParseRelay("token-a", 1, HrPacketParser.KindSample);
            Assert.That(ingest.Evaluate(in first, out _),
                Is.EqualTo(HrRelayIngest.Decision.AcceptSample));

            var wrong = ParseRelay("token-b", 2, HrPacketParser.KindSample);
            var decision = ingest.Evaluate(in wrong, out _);
            Assert.That(decision, Is.EqualTo(HrRelayIngest.Decision.Reject));
            Assert.That(HrRelayAckProtocol.ShouldSendAcceptedAck(in wrong, decision), Is.False);
        }

        [Test]
        public void UnityAndroidManifestExplicitlyContainsInternetPermissions()
        {
            string manifestPath = Path.Combine(Application.dataPath,
                "Plugins/Android/AndroidManifest.xml");
            string manifest = File.ReadAllText(manifestPath);
            Assert.That(manifest, Does.Contain("android.permission.INTERNET"));
            Assert.That(manifest, Does.Contain("android.permission.ACCESS_NETWORK_STATE"));
        }

        [Test]
        public void PlayerSettingsForceInternetPermissionIsEnabled()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string settings = File.ReadAllText(Path.Combine(projectRoot,
                "ProjectSettings/ProjectSettings.asset"));
            Assert.That(settings, Does.Contain("ForceInternetPermission: 1"));
        }
    }
}
