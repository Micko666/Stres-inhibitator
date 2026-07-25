using NUnit.Framework;
using StressTraining.HR;

namespace StressTraining.Tests.EditMode
{
    /// <summary>
    /// Standalone HR pipeline — ingestion gate (spec §8/§9/§10). Verifies session
    /// identity, dedup, out-of-order and stale-watch rejection, sender-restart
    /// recovery, and the heartbeat/sample split at the parser + ingest boundary.
    /// </summary>
    public sealed class HrRelayIngestTests
    {
        private static HrPacketParser.Result Sample(string token, int seq, int bpm, string ts)
        {
            string json = "{\"protocolVersion\":1,\"sessionToken\":\"" + token +
                          "\",\"sequence\":" + seq + ",\"timestampUtc\":\"" + ts +
                          "\",\"bpm\":" + bpm + ",\"source\":\"phone-relay\",\"kind\":\"sample\"}";
            return HrPacketParser.Parse(json);
        }

        private static HrPacketParser.Result Heartbeat(string token, int seq)
        {
            string json = "{\"protocolVersion\":1,\"sessionToken\":\"" + token +
                          "\",\"sequence\":" + seq + ",\"timestampUtc\":\"2026-07-14T08:00:00Z\"," +
                          "\"bpm\":0,\"source\":\"phone-relay\",\"kind\":\"heartbeat\"}";
            return HrPacketParser.Parse(json);
        }

        [Test]
        public void HeartbeatParsesWithoutBpmAndIsNeverASample()
        {
            var hb = Heartbeat("tok", 0);
            Assert.That(hb.Ok, Is.True);
            Assert.That(hb.IsHeartbeat, Is.True);

            var ingest = new HrRelayIngest();
            Assert.That(ingest.Evaluate(in hb, out _), Is.EqualTo(HrRelayIngest.Decision.Heartbeat));
            Assert.That(ingest.HeartbeatCount, Is.EqualTo(1));
        }

        [Test]
        public void FirstTokenBindsAndDifferentTokenIsRejected()
        {
            var ingest = new HrRelayIngest();
            var a = Sample("phoneA", 0, 70, "2026-07-14T08:00:00Z");
            Assert.That(ingest.Evaluate(in a, out _), Is.EqualTo(HrRelayIngest.Decision.AcceptSample));
            Assert.That(ingest.BoundToken, Is.EqualTo("phoneA"));

            var b = Sample("phoneB", 1, 71, "2026-07-14T08:00:01Z");
            Assert.That(ingest.Evaluate(in b, out var reason), Is.EqualTo(HrRelayIngest.Decision.Reject));
            Assert.That(reason, Does.Contain("token"));
            Assert.That(ingest.WrongTokenCount, Is.EqualTo(1));
        }

        [Test]
        public void DuplicateSequenceIsRejected()
        {
            var ingest = new HrRelayIngest();
            var s1 = Sample("t", 5, 70, "2026-07-14T08:00:00Z");
            var s2 = Sample("t", 5, 71, "2026-07-14T08:00:05Z");
            Assert.That(ingest.Evaluate(in s1, out _), Is.EqualTo(HrRelayIngest.Decision.AcceptSample));
            Assert.That(ingest.Evaluate(in s2, out var reason), Is.EqualTo(HrRelayIngest.Decision.Reject));
            Assert.That(reason, Does.Contain("sequence"));
        }

        [Test]
        public void OutOfOrderSequenceIsRejected()
        {
            var ingest = new HrRelayIngest();
            var s10 = Sample("t", 10, 70, "2026-07-14T08:00:10Z");
            var s9 = Sample("t", 9, 71, "2026-07-14T08:00:11Z");
            ingest.Evaluate(in s10, out _);
            Assert.That(ingest.Evaluate(in s9, out _), Is.EqualTo(HrRelayIngest.Decision.Reject));
            Assert.That(ingest.DuplicateOrOutOfOrderCount, Is.EqualTo(1));
        }

        [Test]
        public void StaleWatchTimestampIsRejectedEvenWhenSequenceAdvances()
        {
            var ingest = new HrRelayIngest();
            var newer = Sample("t", 1, 70, "2026-07-14T08:00:10Z");
            var olderWatch = Sample("t", 2, 71, "2026-07-14T08:00:05Z"); // seq ok, watch older
            Assert.That(ingest.Evaluate(in newer, out _), Is.EqualTo(HrRelayIngest.Decision.AcceptSample));
            Assert.That(ingest.Evaluate(in olderWatch, out var reason), Is.EqualTo(HrRelayIngest.Decision.Reject));
            Assert.That(reason, Does.Contain("watch"));
            Assert.That(ingest.StaleWatchCount, Is.EqualTo(1));
        }

        [Test]
        public void SenderRestartAtSequenceZeroRecovers()
        {
            var ingest = new HrRelayIngest();
            for (int i = 0; i < 20; i++)
            {
                var s = Sample("t", i, 70 + (i % 3), "2026-07-14T08:00:" + (10 + i).ToString("00") + "Z");
                ingest.Evaluate(in s, out _);
            }
            // Companion restarts: sequence back to 0, watch clock continues.
            var restart = Sample("t", 0, 75, "2026-07-14T08:05:00Z");
            Assert.That(ingest.Evaluate(in restart, out _), Is.EqualTo(HrRelayIngest.Decision.AcceptSample),
                "a fresh sender session (seq 0) must not be permanently locked out");
        }

        [Test]
        public void PythonBridgePacketWithoutTokenStillFlowsThroughIngest()
        {
            // Legacy/dev bridge carries no session token; identity check is skipped.
            string json = "{\"schemaVersion\":2,\"sequence\":1,\"hr\":72," +
                          "\"sourceTimestampUtc\":\"2026-07-14T08:00:00Z\",\"source\":\"python-bridge\"}";
            var parsed = HrPacketParser.Parse(json);
            var ingest = new HrRelayIngest();
            Assert.That(ingest.Evaluate(in parsed, out _), Is.EqualTo(HrRelayIngest.Decision.AcceptSample));
        }
    }
}
