using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using StressTraining.Core;
using StressTraining.Data;
using StressTraining.HR;
using StressTraining.Session;
using UnityEngine;

namespace StressTraining.Tests.EditMode
{
    /// <summary>
    /// FAZA 8 — HR mode architecture (spec §35) and wrist-watch placeholder
    /// (spec §36): honest source states, leak-free mode switching/shutdown,
    /// and the participant/developer display contract.
    /// </summary>
    public sealed class FableHrArchitectureTests
    {
        // Participant watch: numeric BPM only during baseline, zone otherwise.
        [Test]
        public void ParticipantSuffix_ShowsBpmOnlyDuringBaselineOnRealLink()
        {
            // Real link + receiving + baseline allowed -> exact BPM.
            Assert.That(WristWatchDisplay.ParticipantSuffix(true, true, 85, "Stabilno", true),
                Is.EqualTo("85 bpm"));
            // Real link + receiving but NOT baseline -> zone (no number).
            Assert.That(WristWatchDisplay.ParticipantSuffix(true, true, 85, "Stabilno", false),
                Is.EqualTo("Stabilno"));
            // Simulated/placeholder (no participant link) -> never a number.
            Assert.That(WristWatchDisplay.ParticipantSuffix(false, true, 85, "", true),
                Is.EqualTo(""));
            // Signal lost -> zone label, even if baseline would allow numbers.
            Assert.That(WristWatchDisplay.ParticipantSuffix(true, false, 85, "Signal izgubljen", true),
                Is.EqualTo("Signal izgubljen"));
        }

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private Transform NewAnchor(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go.transform;
        }

        private static (HeartRateService service, SimulatedHeartRateSource sim,
            NativeAdbHeartRateSource adb) NewService()
        {
            var service = new HeartRateService(new HrZoneConfig(), new AppErrorService());
            var sim = new SimulatedHeartRateSource { NoiseAmplitude = 0, BaseBpm = 72 };
            var adb = new NativeAdbHeartRateSource();
            service.ConfigureSources(sim, null, adb);   // network variant tested separately
            return (service, sim, adb);
        }

        private WristWatchDisplay NewWatch(Transform anchor, Transform fallback,
            HeartRateService service, bool developerMode)
        {
            var watch = WristWatchDisplay.CreateOrFind(anchor, fallback, service, developerMode);
            _spawned.Add(watch.gameObject);
            return watch;
        }

        // 1. Simulated source is clearly labelled.
        [Test]
        public void SimulatedMode_DevLine_CarriesSimulatedDataLabel()
        {
            var (service, _, _) = NewService();
            service.SetMode(HeartRateMode.Simulated);
            var watch = NewWatch(NewAnchor("LeftAnchor"), null, service, developerMode: true);

            service.Update(1.0);   // pumps one simulated sample
            watch.RefreshNow();

            Assert.That(service.ActiveSource.SourceType, Is.EqualTo(HrSourceType.Simulated));
            Assert.That(watch.DevLine, Does.Contain("SIMULIRANI PODACI"));
        }

        // 2. Disconnected source never emits a valid HR sample.
        [Test]
        public void DisconnectedMode_EmitsNothing_AndWatchSaysNotConnected()
        {
            var (service, _, _) = NewService();
            int accepted = 0;
            service.SampleAccepted += _ => accepted++;
            service.SetMode(HeartRateMode.Disconnected);
            var watch = NewWatch(NewAnchor("LeftAnchor"), null, service, developerMode: false);

            service.Update(2.0);
            watch.RefreshNow();

            Assert.That(accepted, Is.Zero);
            Assert.That(service.ActiveSource, Is.Null);
            Assert.That(service.CurrentBpm, Is.EqualTo(-1));
            Assert.That(service.IsReceiving, Is.False);
            Assert.That(watch.StatusLine, Is.EqualTo("Sat nije povezan"));
        }

        // 3. NativeAdb placeholder never reports Connected.
        [Test]
        public void NativeAdbPlaceholder_NeverClaimsConnection()
        {
            var (service, _, adb) = NewService();
            NativeAdbConnectionState? observed = null;
            adb.StateChanged += s => observed = s;

            bool connected = adb.Connect("192.168.1.222", 5555);

            Assert.That(connected, Is.False);
            Assert.That(adb.State, Is.EqualTo(NativeAdbConnectionState.NotImplemented));
            Assert.That(observed, Is.EqualTo(NativeAdbConnectionState.NotImplemented));
            Assert.That(adb.LastError, Is.Not.Empty);

            service.SetMode(HeartRateMode.NativeAdbPlaceholder);
            int accepted = 0;
            service.SampleAccepted += _ => accepted++;
            service.Update(2.0);
            Assert.That(accepted, Is.Zero, "placeholder must never fabricate samples");

            var watch = NewWatch(NewAnchor("LeftAnchor"), null, service, developerMode: false);
            watch.RefreshNow();
            Assert.That(watch.StatusLine, Is.EqualTo("Sat nije povezan"));
        }

        // 4. Mode switch stops the previous source.
        [Test]
        public void SetMode_StopsPreviousSource()
        {
            var (service, sim, adb) = NewService();

            service.SetMode(HeartRateMode.Simulated);
            Assert.That(sim.IsRunning, Is.True);

            service.SetMode(HeartRateMode.NativeAdbPlaceholder);
            Assert.That(sim.IsRunning, Is.False, "previous source must be stopped");
            Assert.That(adb.IsRunning, Is.True);

            service.SetMode(HeartRateMode.Disconnected);
            Assert.That(adb.IsRunning, Is.False);
            Assert.That(service.ActiveSource, Is.Null);
        }

        // 5. Shutdown leaves nothing running; further updates are inert and safe.
        [Test]
        public void Shutdown_StopsEverySource_AndIsIdempotent()
        {
            var (service, sim, adb) = NewService();
            service.SetMode(HeartRateMode.Simulated);
            service.Update(1.0);
            Assert.That(service.IsReceiving, Is.True);

            service.Shutdown();

            Assert.That(sim.IsRunning, Is.False);
            Assert.That(adb.IsRunning, Is.False);
            Assert.That(service.ActiveSource, Is.Null);
            Assert.That(service.Mode, Is.EqualTo(HeartRateMode.Disconnected));
            Assert.That(service.CurrentBpm, Is.EqualTo(-1));

            int accepted = 0;
            service.SampleAccepted += _ => accepted++;
            service.Update(2.0);
            Assert.That(accepted, Is.Zero);
            Assert.DoesNotThrow(() => service.Shutdown());   // second call is a no-op
        }

        // 6. Participant UI never presents simulation as a connected source.
        [Test]
        public void Watch_ParticipantLine_HidesSimulatedSourceAndRawBpm()
        {
            var (service, _, _) = NewService();
            service.SetMode(HeartRateMode.Simulated);
            var watch = NewWatch(NewAnchor("LeftAnchor"), null, service, developerMode: false);

            service.Update(1.0);
            Assert.That(service.CurrentBpm, Is.GreaterThan(0), "test needs live samples");
            watch.RefreshNow();

            Assert.That(watch.StatusLine, Is.EqualTo("Sat nije povezan"),
                "simulation must not appear as a participant-selectable production source");
            Assert.That(watch.StatusLine.Any(char.IsDigit), Is.False,
                "raw BPM must never reach the participant display");
            Assert.That(watch.DevLine, Is.Empty, "no developer line outside developer mode");
        }

        // 7. Developer mode may show BPM, mode and the simulated-data label.
        [Test]
        public void Watch_DeveloperLine_ShowsBpmAndMode()
        {
            var (service, _, _) = NewService();
            service.SetMode(HeartRateMode.Simulated);
            var watch = NewWatch(NewAnchor("LeftAnchor"), null, service, developerMode: true);

            service.Update(1.0);
            watch.RefreshNow();

            Assert.That(watch.DevLine, Does.Contain("bpm"));
            Assert.That(watch.DevLine.Any(char.IsDigit), Is.True);
            Assert.That(watch.DevLine, Does.Contain(HeartRateMode.Simulated.ToString()));
            Assert.That(watch.DevLine, Does.Contain("SIMULIRANI PODACI"));
        }

        // 8. Missing controller anchor uses the fallback anchor.
        [Test]
        public void Watch_MissingControllerAnchor_UsesFallback()
        {
            var (service, _, _) = NewService();
            var fallback = NewAnchor("RigRootFallback");

            var watch = NewWatch(null, fallback, service, developerMode: false);

            Assert.That(watch.transform.parent, Is.EqualTo(fallback));
            Assert.That(watch.UsedFallbackAnchor, Is.True);
            Assert.That(watch.transform.childCount, Is.GreaterThan(0), "visuals must still build");
        }

        // 9. Repeated bootstrap does not create a second watch.
        [Test]
        public void RepeatedBootstrap_ReusesExistingWatch()
        {
            var (service, _, _) = NewService();
            var anchor = NewAnchor("LeftAnchor");

            var first = NewWatch(anchor, null, service, developerMode: false);
            int builtChildren = first.transform.childCount;
            var second = WristWatchDisplay.CreateOrFind(anchor, null, service, false);

            Assert.That(second, Is.SameAs(first));
            Assert.That(first.transform.childCount, Is.EqualTo(builtChildren),
                "re-initialization must not rebuild visuals");
            Assert.That(anchor.Cast<Transform>().Count(
                t => t.GetComponent<WristWatchDisplay>() != null), Is.EqualTo(1));
        }


        [Test]
        public void SimulatedOrDeveloperSession_IsNeverSchedulerEligible()
        {
            Assert.That(ProductionSessionFlow.IsSchedulerEligibleForProductionSession(
                false, HrSourceType.Simulated, BaselineQuality.Good), Is.False);
            Assert.That(ProductionSessionFlow.IsSchedulerEligibleForProductionSession(
                true, HrSourceType.NetworkBridge, BaselineQuality.Good), Is.False);
            Assert.That(ProductionSessionFlow.IsSchedulerEligibleForProductionSession(
                false, HrSourceType.NetworkBridge, BaselineQuality.Low), Is.False);
            Assert.That(ProductionSessionFlow.IsSchedulerEligibleForProductionSession(
                false, HrSourceType.NetworkBridge, BaselineQuality.Good), Is.True);
        }

        [Test]
        public void ProductionDefaults_DoNotEnableSimulatedFallbackOrHrBypass()
        {
            var config = new StressTrainingConfig();
            Assert.That(config.developer.useSimulatedHeartRate, Is.False);
            Assert.That(config.baseline.allowContinueWithoutHrInDevMode, Is.False);
            Assert.That(config.developer.useShortDevBaseline, Is.False);
        }

        // 10. Network fallback stays available and shuts down cleanly.
        [Test]
        public void NetworkMode_RemainsAvailable_AndShutdownStopsIt()
        {
            var service = new HeartRateService(new HrZoneConfig(), new AppErrorService());
            var sim = new SimulatedHeartRateSource();
            var network = new NetworkHeartRateSource("127.0.0.1", 0);   // ephemeral port
            var adb = new NativeAdbHeartRateSource();
            service.ConfigureSources(sim, network, adb);

            service.SetMode(HeartRateMode.Network);
            Assert.That(service.ActiveSource, Is.SameAs(network));
            Assert.That(network.SourceType, Is.EqualTo(HrSourceType.NetworkBridge));
            Assert.That(network.IsRunning, Is.True);

            service.Shutdown();
            Assert.That(network.IsRunning, Is.False, "UDP thread must not outlive shutdown");
        }
    }
}
