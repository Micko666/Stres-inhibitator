using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using StressTraining.Console;
using StressTraining.Core;
using StressTraining.Data;
using StressTraining.Persistence;
using StressTraining.Session;
using StressTraining.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace StressTraining.Tests.PlayMode
{
    public sealed class VerticalSliceSmokeTests
    {
        [UnityTest]
        public IEnumerator ThreeTaskTutorialPracticeBlockSummary_RunWithoutHeadset()
        {
            string temp = Path.Combine(Path.GetTempPath(), "StressTrainingPlayMode",
                System.Guid.NewGuid().ToString("N"));
            var appRoot = new GameObject("TestAppRoot");
            var systems = new GameObject("TestSystemsRoot");
            var ui = new GameObject("TestRuntimeUIRoot");
            var corridor = new GameObject("Corridor_Blockout");
            var spawn = new GameObject("CorridorSpawn").transform;
            spawn.SetParent(corridor.transform, false);
            var consoleShell = new GameObject("Console_BlenderPrototype");
            consoleShell.transform.SetParent(corridor.transform, false);

            var rigObject = new GameObject("TestRig");
            var rigType = System.Type.GetType("OVRCameraRig, Oculus.VR");
            Assert.That(rigType, Is.Not.Null);
            rigObject.AddComponent(rigType);
            var rig = rigObject.transform;

            var bootstrap = appRoot.AddComponent<AppBootstrapper>();
            bootstrap.ConfigureSceneReferences(rig, corridor, spawn, systems.transform, ui.transform);
            bootstrap.ConfigurePersistentDataPathForTests(temp);
            yield return null;

            Assert.That(bootstrap.IsInitialized, Is.True);
            var coordinator = bootstrap.Coordinator;
            var runner = ServiceRegistry.Get<TaskRunner>();
            var input = ServiceRegistry.Get<ConsoleInputRouter>();

            Assert.That(coordinator.CurrentState, Is.EqualTo(AppState.ProfileSelection));
            Assert.That(coordinator.CurrentDemoStage, Is.EqualTo(DemoFlowStage.ProfileSelection));
            Assert.That(GameObject.Find(SafeSpaceBuilder.RootName).activeSelf, Is.True);
            Assert.That(Object.FindObjectsByType<BaseInputModule>(FindObjectsSortMode.None)
                .Any(module => module.GetType().Name == "OVRInputModule"), Is.True);
            Assert.That(Object.FindObjectsByType<BaseRaycaster>(FindObjectsSortMode.None)
                .Any(raycaster => raycaster.GetType().Name == "OVRRaycaster"), Is.True);
            var pokeSystem = ServiceRegistry.Get<QuestControllerPokeSystem>();
            Assert.That(pokeSystem.IsInitialized, Is.True,
                "The no-HMD smoke rig must construct both official Meta poke interactors.");
            OVRControllerPokeSource[] pokeSources = Object.FindObjectsByType<OVRControllerPokeSource>(
                FindObjectsSortMode.None);
            Assert.That(pokeSources.Length, Is.EqualTo(2));
            TestContext.WriteLine(
                "Poke construction verified without HMD; live hover/select/haptics require Quest hardware.");
            Assert.That(ServiceRegistry.Get<StressTraining.UI.UIManager>()
                .MainCanvas.transform.lossyScale.x, Is.GreaterThan(0));

            var profile = coordinator.CreateProfile("Smoke " +
                System.Guid.NewGuid().ToString("N").Substring(0, 6));
            Assert.That(profile, Is.Not.Null);
            string realCycleIdBeforeDemo = profile.activeCycleId;
            int cycleIndexBeforeDemo = profile.currentCycleSessionIndex;
            int cycleSummaryCountBeforeDemo = profile.cycleSummaries.Count;
            string lastSessionBeforeDemo = profile.lastSessionAtUtcIso;
            string nextRecommendationBeforeDemo = profile.nextRecommendedSessionAtUtcIso;
            int nBackLevelBeforeDemo = profile.currentNBackLevel;
            int goNoGoLevelBeforeDemo = profile.currentGoNoGoLevel;
            int flankerLevelBeforeDemo = profile.currentFlankerLevel;
            Assert.That(coordinator.CurrentDemoStage, Is.EqualTo(DemoFlowStage.DemoOverview));
            Assert.That(runner.IsActive, Is.False,
                "Selecting a profile must not auto-start a cognitive task.");

            coordinator.StartDemo();
            Assert.That(coordinator.CurrentState, Is.EqualTo(AppState.ActiveSession));
            Assert.That(coordinator.CurrentDemoStage, Is.EqualTo(DemoFlowStage.NBackTutorial));
            Assert.That(runner.IsActive, Is.False,
                "Pokreni demo must open the tutorial, not skip into trials.");
            Assert.That(corridor.activeSelf, Is.False,
                "Tutorials must stay on the shared SafeSpace UI anchor.");
            Assert.That(ServiceRegistry.Get<SceneZoneController>().CurrentZone,
                Is.EqualTo(SceneZoneController.Zone.SafeSpace));

            RunTaskFlow(coordinator, runner, input, TaskType.NBack,
                DemoFlowStage.NBackTutorial, DemoFlowStage.GoNoGoTutorial,
                exercisePause: true, demonstrateGoNoGoErrors: false);
            RunTaskFlow(coordinator, runner, input, TaskType.GoNoGo,
                DemoFlowStage.GoNoGoTutorial, DemoFlowStage.FlankerTutorial,
                exercisePause: false, demonstrateGoNoGoErrors: true);
            RunTaskFlow(coordinator, runner, input, TaskType.Flanker,
                DemoFlowStage.FlankerTutorial, DemoFlowStage.SessionSummary,
                exercisePause: false, demonstrateGoNoGoErrors: false);

            Assert.That(coordinator.CurrentState, Is.EqualTo(AppState.PostSessionSummary));
            Assert.That(coordinator.CurrentDemoStage, Is.EqualTo(DemoFlowStage.SessionSummary));
            Assert.That(coordinator.LastSummary, Is.Not.Null);
            Assert.That(coordinator.LastSummary.completionStatus,
                Is.EqualTo(CompletionStatus.Completed));
            Assert.That(coordinator.LastSummary.taskSummaries.Count, Is.EqualTo(3));
            CollectionAssert.AreEquivalent(
                new[] { TaskType.NBack, TaskType.GoNoGo, TaskType.Flanker },
                coordinator.LastSummary.taskSummaries.Select(x => x.taskType).ToArray());
            Assert.That(GameObject.Find(SafeSpaceBuilder.RootName).activeSelf, Is.True);
            Assert.That(corridor.activeSelf, Is.False);

            var sessions = ServiceRegistry.Get<SessionRepository>();
            SessionSummaryData persistedSummary = sessions.LoadSessionSummary(profile.userId,
                coordinator.LastSummary.sessionId);
            Assert.That(persistedSummary, Is.Not.Null);
            AssertScoredSummary(persistedSummary, TaskType.NBack, 8);
            AssertScoredSummary(persistedSummary, TaskType.GoNoGo, 10);
            AssertScoredSummary(persistedSummary, TaskType.Flanker, 10);

            // Demo results remain in history but never advance the real cycle or
            // create a next-session scheduler recommendation.
            UserProfileData persistedProfile = ServiceRegistry.Get<ProfileRepository>()
                .LoadProfile(profile.userId);
            Assert.That(persistedProfile.currentCycleSessionIndex, Is.EqualTo(cycleIndexBeforeDemo));
            Assert.That(persistedProfile.activeCycleId, Is.EqualTo(realCycleIdBeforeDemo));
            Assert.That(persistedProfile.cycleSummaries.Count,
                Is.EqualTo(cycleSummaryCountBeforeDemo));
            Assert.That(persistedProfile.lastSessionAtUtcIso, Is.EqualTo(lastSessionBeforeDemo));
            Assert.That(persistedProfile.nextRecommendedSessionAtUtcIso,
                Is.EqualTo(nextRecommendationBeforeDemo));
            Assert.That(persistedProfile.currentNBackLevel, Is.EqualTo(nBackLevelBeforeDemo));
            Assert.That(persistedProfile.currentGoNoGoLevel, Is.EqualTo(goNoGoLevelBeforeDemo));
            Assert.That(persistedProfile.currentFlankerLevel, Is.EqualTo(flankerLevelBeforeDemo));
            Assert.That(persistedProfile.sessionSummaries.Any(x =>
                x.sessionId == persistedSummary.sessionId), Is.True);
            Assert.That(persistedSummary.cycleId,
                Does.StartWith("demo-"), "Demo persistence must use a synthetic cycle id.");
            Assert.That(persistedSummary.sessionNumberInCycle, Is.Zero);

            var paths = ServiceRegistry.Get<PersistencePaths>();
            TrialRecord[] trials = ReadJsonLines<TrialRecord>(
                paths.TrialsFile(profile.userId, persistedSummary.sessionId));
            Assert.That(trials.Length, Is.EqualTo(29),
                "Expected 28 scored trials plus one N-back warm-up.");
            Assert.That(trials.Any(x => !string.IsNullOrEmpty(x.blockId) &&
                x.blockId.StartsWith("practice-", System.StringComparison.Ordinal)), Is.False,
                "Practice trials must never enter trials.jsonl.");
            AssertPersistedBlock(trials, TaskType.NBack, 8, 1);
            AssertPersistedBlock(trials, TaskType.GoNoGo, 10, 0);
            AssertPersistedBlock(trials, TaskType.Flanker, 10, 0);

            HeartRateSampleRecord[] hr = ReadJsonLines<HeartRateSampleRecord>(
                paths.HrFile(profile.userId, persistedSummary.sessionId));
            Assert.That(hr, Is.Not.Empty);
            Assert.That(hr.All(x => x.sourceType == HrSourceType.Simulated), Is.True,
                "Every development HR sample must retain Simulated provenance.");
            foreach (TaskSessionSummaryData taskSummary in persistedSummary.taskSummaries)
            {
                Assert.That(taskSummary.avgBpmDuringTask, Is.EqualTo(-1f));
                Assert.That(taskSummary.maxBpmDuringTask, Is.EqualTo(-1f));
                Assert.That(taskSummary.elevatedOrHighZoneSeconds, Is.Zero);
            }

            Object.Destroy(appRoot);
            Object.Destroy(systems);
            Object.Destroy(ui);
            Object.Destroy(corridor);
            Object.Destroy(rig.gameObject);
            yield return null;
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }

        private static void RunTaskFlow(SessionCoordinator coordinator, TaskRunner runner,
            ConsoleInputRouter input, TaskType task, DemoFlowStage expectedTutorial,
            DemoFlowStage expectedNextStage, bool exercisePause,
            bool demonstrateGoNoGoErrors)
        {
            Assert.That(coordinator.CurrentDemoStage, Is.EqualTo(expectedTutorial));
            Assert.That(coordinator.CurrentDemoTask, Is.EqualTo(task));

            coordinator.BeginPractice();
            Assert.That(runner.IsPractice, Is.True);
            Assert.That(ServiceRegistry.Get<SceneZoneController>().CurrentZone,
                Is.EqualTo(SceneZoneController.Zone.Corridor),
                "Entering practice must move the user to the physical console.");

            if (exercisePause)
            {
                coordinator.Tick(1.0);
                double beforePause = coordinator.ActiveSession.Clock.ActiveElapsedSeconds;
                coordinator.PauseSession();
                coordinator.Tick(1.0);
                Assert.That(coordinator.ActiveSession.Clock.ActiveElapsedSeconds,
                    Is.EqualTo(beforePause).Within(0.001));
                coordinator.BeginResumeCountdown();
                double activeBeforeCountdown = coordinator.ActiveSession.Clock.ActiveElapsedSeconds;
                double pausedBeforeCountdown = coordinator.ActiveSession.Clock.PausedElapsedSeconds;
                coordinator.Tick(4.0);
                Assert.That(coordinator.CurrentState, Is.EqualTo(AppState.ActiveSession));
                Assert.That(coordinator.ActiveSession.Clock.ActiveElapsedSeconds,
                    Is.GreaterThan(activeBeforeCountdown),
                    "Countdown frame overshoot must become active time after resume.");
                Assert.That(coordinator.ActiveSession.Clock.PausedElapsedSeconds - pausedBeforeCountdown,
                    Is.LessThan(4.0),
                    "Only the countdown portion of an overshooting frame is paused time.");
            }

            CompleteActiveRun(coordinator, runner, input,
                demonstrateGoNoGoErrors && task == TaskType.GoNoGo);
            Assert.That(runner.LastResult.IsPractice, Is.True);
            Assert.That(runner.LastResult.ContributesToSessionScore, Is.False);
            if (demonstrateGoNoGoErrors && task == TaskType.GoNoGo)
            {
                TaskBlockResult practice = runner.LastResult.Block;
                Assert.That(practice.BlockId.StartsWith(
                    "practice-", System.StringComparison.Ordinal), Is.True);
                Assert.That(practice.ScoreableCount, Is.EqualTo(4));
                Assert.That(practice.CorrectCount, Is.EqualTo(2));
                Assert.That(practice.MissCount, Is.EqualTo(1),
                    "GO trial 2 must demonstrate one omission.");
                Assert.That(practice.FalsePositiveCount, Is.EqualTo(1),
                    "NO-GO trial 3 must demonstrate one commission error.");
                Assert.That(practice.Accuracy, Is.EqualTo(0.5f).Within(0.0001f));
            }
            Assert.That(coordinator.CurrentDemoStage,
                Is.EqualTo(ThreeTaskDemoFlow.PracticeReviewStage(task)));

            coordinator.ContinueAfterPractice();
            Assert.That(coordinator.CurrentDemoStage,
                Is.EqualTo(ThreeTaskDemoFlow.CountdownStage(task)));
            double activeBeforeBlockCountdown = coordinator.ActiveSession.Clock.ActiveElapsedSeconds;
            coordinator.Tick(4.0);
            Assert.That(runner.IsActive, Is.True);
            Assert.That(runner.IsPractice, Is.False);
            Assert.That(coordinator.ActiveSession.Clock.ActiveElapsedSeconds -
                activeBeforeBlockCountdown, Is.EqualTo(4.0).Within(0.001));
            Assert.That(runner.CurrentPhase, Is.Not.EqualTo(TrialPhase.InterTrialInterval),
                "The one-second countdown overshoot must advance the scored task.");

            CompleteActiveRun(coordinator, runner, input, false);
            Assert.That(coordinator.CurrentDemoStage, Is.EqualTo(expectedNextStage));
            Assert.That(ServiceRegistry.Get<SceneZoneController>().CurrentZone,
                Is.EqualTo(SceneZoneController.Zone.SafeSpace),
                "The next tutorial/summary must return to the stable SafeSpace anchor.");
        }

        private static void CompleteActiveRun(SessionCoordinator coordinator, TaskRunner runner,
            ConsoleInputRouter input, bool demonstrateGoNoGoErrors)
        {
            int safety = 0;
            while (runner.IsActive && safety++ < 64)
            {
                coordinator.Tick(2.0); // ITI -> stimulus
                TaskTrialDefinition trial = runner.CurrentTrial;
                if (trial != null && !trial.isWarmup)
                {
                    SemanticAction action = trial.expectedAction;
                    if (demonstrateGoNoGoErrors && trial.index == 2)
                        action = SemanticAction.None; // omission: deliberately miss GO
                    else if (demonstrateGoNoGoErrors && trial.index == 3)
                        action = SemanticAction.Go;   // commission: press on NO-GO

                    if (action != SemanticAction.None)
                        input.InjectAction(action, "playmode_smoke");
                }
                coordinator.Tick(3.0); // response latch or timeout -> feedback
                coordinator.Tick(0.5); // feedback -> next trial / block complete
            }
            Assert.That(safety, Is.LessThan(64), "Task runtime did not finish.");
            Assert.That(runner.IsActive, Is.False);
        }

        private static void AssertScoredSummary(SessionSummaryData summary,
            TaskType task, int expectedTrials)
        {
            TaskSessionSummaryData[] matches = summary.taskSummaries
                .Where(x => x.taskType == task).ToArray();
            Assert.That(matches.Length, Is.EqualTo(1));
            TaskSessionSummaryData result = matches[0];
            Assert.That(result.blockCount, Is.EqualTo(1));
            Assert.That(result.trialCount, Is.EqualTo(expectedTrials));
            Assert.That(result.correctCount, Is.EqualTo(expectedTrials));
            Assert.That(result.missCount, Is.Zero);
            Assert.That(result.falsePositiveCount, Is.Zero);
            Assert.That(result.accuracy, Is.EqualTo(1f).Within(0.0001f));
        }

        private static void AssertPersistedBlock(TrialRecord[] all, TaskType task,
            int scoreable, int warmups)
        {
            TrialRecord[] records = all.Where(x => x.taskType == task).ToArray();
            Assert.That(records.Count(x => !x.isWarmup), Is.EqualTo(scoreable));
            Assert.That(records.Count(x => x.isWarmup), Is.EqualTo(warmups));
            Assert.That(records.Select(x => x.blockId).Distinct().Count(), Is.EqualTo(1),
                "Only the single scored block may be persisted for each task.");
        }

        private static T[] ReadJsonLines<T>(string path) where T : class
        {
            Assert.That(File.Exists(path), Is.True, "Missing persistence file: " + path);
            T[] records = File.ReadAllLines(path)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(JsonUtility.FromJson<T>)
                .ToArray();
            Assert.That(records.All(x => x != null), Is.True,
                "At least one JSONL record could not be parsed.");
            return records;
        }
    }
}
