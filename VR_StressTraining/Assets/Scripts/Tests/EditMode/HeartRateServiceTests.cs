using System.Collections.Generic;
using NUnit.Framework;
using StressTraining.Core;
using StressTraining.Data;
using StressTraining.HR;

namespace StressTraining.Tests.EditMode
{
    public sealed class HeartRateServiceTests
    {
        private sealed class QueuedSource : IHeartRateSource
        {
            private readonly Queue<RawHrSample> _samples = new Queue<RawHrSample>();
            public HrSourceType SourceType { get; set; }
            public bool IsRunning { get; private set; }

            public void StartSource() => IsRunning = true;
            public void StopSource() => IsRunning = false;
            public void Enqueue(int bpm, double signalAgeMs = 0)
            {
                _samples.Enqueue(new RawHrSample
                {
                    Bpm = bpm,
                    ReceivedAtUtcIso = "2026-07-14T08:00:00Z",
                    SourceTimestampUtcIso = "2026-07-14T08:00:00Z",
                    Sequence = _samples.Count + 1,
                    SignalAgeMs = signalAgeMs,
                    ProtocolVersion = 1,
                    Source = "test-network"
                });
            }

            public int DrainSamples(List<RawHrSample> buffer)
            {
                int count = 0;
                while (_samples.Count > 0)
                {
                    buffer.Add(_samples.Dequeue());
                    count++;
                }
                return count;
            }
        }

        [Test]
        public void SimulatedSignal_BecomesStaleOnOneMonotonicClock()
        {
            var cfg = new HrZoneConfig { staleSignalSeconds = 0.5f };
            var source = new SimulatedHeartRateSource { NoiseAmplitude = 0 };
            Assert.That(source.SourceType, Is.EqualTo(HrSourceType.Simulated));
            var service = new HeartRateService(cfg, new AppErrorService());
            service.SetSource(source);
            service.Update(1.0);
            Assert.That(service.IsReceiving, Is.True);

            source.SetScenario(SimulatedHrScenario.SignalLoss);
            service.Update(0.6);
            Assert.That(service.IsReceiving, Is.False);
            Assert.That(service.CurrentZone, Is.EqualTo(HrZone.SignalLost));
        }

        [Test]
        public void PausedSample_IsTaggedAndExcludedFromActiveAverage()
        {
            var source = new SimulatedHeartRateSource { NoiseAmplitude = 0, BaseBpm = 70 };
            var service = new HeartRateService(new HrZoneConfig(), new AppErrorService());
            HeartRateSampleRecord accepted = null;
            service.SampleAccepted += sample => accepted = sample;
            service.AttachSession("session", null, () => (true, "Paused", TaskType.NBack, "b", "t"));
            service.SetSource(source);
            service.Update(1.0);

            Assert.That(accepted, Is.Not.Null);
            Assert.That(accepted.sourceType, Is.EqualTo(HrSourceType.Simulated));
            Assert.That(accepted.isPaused, Is.True);
            Assert.That(service.SessionActiveSampleCount, Is.Zero);
            Assert.That(service.SessionAverageBpm, Is.EqualTo(-1f));
        }

        [Test]
        public void ProductionGate_RejectsDisconnectedSimulatedAndNativePlaceholder()
        {
            var service = new HeartRateService(new HrZoneConfig(), new AppErrorService());
            Assert.That(service.GetProductionReadiness().IsReady, Is.False);

            service.SetSource(new SimulatedHeartRateSource());
            service.Update(1.0);
            Assert.That(service.GetProductionReadiness().IsReady, Is.False);

            service.SetSource(new NativeAdbHeartRateSource());
            service.Update(1.0);
            Assert.That(service.GetProductionReadiness().IsReady, Is.False);
        }

        [Test]
        public void ProductionGate_RejectsNetworkUntilFreshValidSampleArrives()
        {
            var source = new QueuedSource { SourceType = HrSourceType.NetworkBridge };
            var service = new HeartRateService(
                new HrZoneConfig { staleSignalSeconds = 2f }, new AppErrorService());
            service.SetSource(source);

            Assert.That(service.GetProductionReadiness().IsReady, Is.False);
            Assert.That(service.GetProductionReadiness().IsRealExternalSource, Is.True);

            source.Enqueue(76);
            service.Update(0.1);
            ProductionHrReadiness ready = service.GetProductionReadiness();
            Assert.That(ready.IsReady, Is.True);
            Assert.That(ready.HasFreshValidSample, Is.True);
        }

        [Test]
        public void ProductionGate_RejectsStaleOrImplausibleNetworkSamples()
        {
            var source = new QueuedSource { SourceType = HrSourceType.NetworkBridge };
            var service = new HeartRateService(
                new HrZoneConfig
                {
                    staleSignalSeconds = 1f,
                    minPlausibleBpm = 35,
                    maxPlausibleBpm = 210
                }, new AppErrorService());
            service.SetSource(source);

            source.Enqueue(76, signalAgeMs: 5000);
            service.Update(0.1);
            Assert.That(service.GetProductionReadiness().IsReady, Is.False,
                "source timestamps older than the quality window are rejected");

            source.Enqueue(500);
            service.Update(0.1);
            Assert.That(service.GetProductionReadiness().IsReady, Is.False,
                "implausible values cannot replace the last quality-good sample");
        }


        [Test]
        public void PhoneRelayProtocolV1_RequiresSessionToken()
        {
            string valid = "{\"protocolVersion\":1,\"sessionToken\":\"paired-abc\"," +
                           "\"sequence\":123,\"timestampUtc\":\"2026-07-14T08:00:00Z\"," +
                           "\"bpm\":76,\"source\":\"phone-relay\"}";
            HrPacketParser.Result parsed = HrPacketParser.Parse(valid);
            Assert.That(parsed.Ok, Is.True);
            Assert.That(parsed.ProtocolVersion, Is.EqualTo(1));
            Assert.That(parsed.SessionToken, Is.EqualTo("paired-abc"));
            Assert.That(parsed.Sequence, Is.EqualTo(123));

            string missingToken = "{\"protocolVersion\":1,\"sessionToken\":\"\"," +
                                  "\"sequence\":124,\"timestampUtc\":\"2026-07-14T08:00:01Z\"," +
                                  "\"bpm\":77,\"source\":\"phone-relay\"}";
            Assert.That(HrPacketParser.Parse(missingToken).Ok, Is.False);
        }

        [Test]
        public void ExistingPythonBridgePacket_RemainsProtocolV1Compatible()
        {
            string packet = "{\"schemaVersion\":2,\"sequence\":42,\"hr\":75," +
                            "\"sourceTimestampUtc\":\"2026-07-14T08:00:00Z\"," +
                            "\"sentAtUtc\":\"2026-07-14T08:00:00Z\",\"source\":\"python-bridge\"}";
            HrPacketParser.Result parsed = HrPacketParser.Parse(packet);
            Assert.That(parsed.Ok, Is.True);
            Assert.That(parsed.ProtocolVersion, Is.EqualTo(NetworkHeartRateSource.ProtocolVersion));
            Assert.That(parsed.Bpm, Is.EqualTo(75));
            Assert.That(parsed.Source, Is.EqualTo("python-bridge"));
        }

        [Test]
        public void ProductionGate_ClosesWhenLastGoodNetworkSampleBecomesLocallyStale()
        {
            var source = new QueuedSource { SourceType = HrSourceType.NetworkBridge };
            var service = new HeartRateService(
                new HrZoneConfig { staleSignalSeconds = 0.5f }, new AppErrorService());
            service.SetSource(source);
            source.Enqueue(72);
            service.Update(0.1);
            Assert.That(service.CanUnlockProductionBaseline, Is.True);

            service.Update(0.6);
            Assert.That(service.CanUnlockProductionBaseline, Is.False);
            Assert.That(service.IsReceiving, Is.False);
        }
    }
}
