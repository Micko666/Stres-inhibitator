using System.IO;
using NUnit.Framework;
using StressTraining.Core;
using StressTraining.Data;
using StressTraining.Persistence;
using StressTraining.Session;

namespace StressTraining.Tests.EditMode
{
    /// <summary>
    /// Voluntary abort of an active production session: the participant leaves the
    /// headset immediately, everything recorded so far is kept, nothing adapts, and
    /// the attempt does not consume a place in the training cycle.
    /// </summary>
    public class SessionAbortFlowTests
    {
        private string _temp;
        private UserProfileService _service;
        private ProfileRepository _repo;

        [SetUp]
        public void SetUp()
        {
            _temp = Path.Combine(Path.GetTempPath(), "asc_abort_" + Path.GetRandomFileName());
            var paths = new PersistencePaths(_temp);
            paths.EnsureBaseDirectories();
            var errors = new AppErrorService();
            _repo = new ProfileRepository(paths, new SchemaMigrationService(),
                new BackupRecoveryService(paths), errors);
            _service = new UserProfileService(_repo, new SessionConfig());
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_temp)) Directory.Delete(_temp, true); } catch { }
        }

        private UserProfileData NewProfile()
        {
            var p = _service.CreateProfile("ispitanik");
            _service.EnsureActiveCycle(p);
            return p;
        }

        private static SessionSummaryData Finished(string id, ValidityStatus validity,
            CompletionStatus completion = CompletionStatus.Completed,
            UserTerminationReason reason = UserTerminationReason.None)
        {
            return new SessionSummaryData
            {
                sessionId = id,
                completionStatus = completion,
                userTerminationReason = reason,
                validityStatus = validity,
                endedAtUtcIso = UtcTime.NowIso()
            };
        }

        // ── 1-2. the closing path itself ─────────────────────────────────────

        [Test]
        public void VoluntaryAbort_SkipsRecoveryAndAllPostQuestionnaires()
        {
            Assert.AreEqual(SessionEndPath.StraightToValidity,
                SessionEndPolicy.PathFor(preCorridor: false, voluntaryAbort: true));
        }

        [Test]
        public void NormalCompletion_KeepsRecoveryAndPostMeasurements()
        {
            Assert.AreEqual(SessionEndPath.RecoveryThenQuestionnaires,
                SessionEndPolicy.PathFor(preCorridor: false, voluntaryAbort: false));
        }

        [Test]
        public void TimeExpired_KeepsRecoveryAndPostMeasurements()
        {
            // TimeExpired is not a voluntary abort: the participant went through the
            // whole active challenge, so the recovery measurement still carries meaning.
            Assert.AreEqual(SessionEndPath.RecoveryThenQuestionnaires,
                SessionEndPolicy.PathFor(preCorridor: false, voluntaryAbort: false));
        }

        [Test]
        public void PreCorridorAbort_StillSkipsRecoveryOnly()
        {
            Assert.AreEqual(SessionEndPath.QuestionnairesOnly,
                SessionEndPolicy.PathFor(preCorridor: true, voluntaryAbort: false));
        }

        // ── 3-4. reason → validity status ────────────────────────────────────

        [Test]
        public void SimulatorSicknessAbort_IsInvalidSimulatorSickness_WithoutPostSsq()
        {
            var v = new SessionValidityEvaluator();
            var input = new SessionValidityEvaluator.Input
            {
                CompletionStatus = CompletionStatus.UserTerminated,
                UserReason = UserTerminationReason.SimulatorSickness,
                SsqPreTotal = 12f,
                SsqPostTotal = -1f              // post-SSQ was skipped on purpose
            };
            Assert.AreEqual(ValidityStatus.InvalidSimulatorSickness, v.Evaluate(input));
        }

        [Test]
        public void PlainVoluntaryAbort_IsIncompleteUserTerminated()
        {
            var v = new SessionValidityEvaluator();
            var input = new SessionValidityEvaluator.Input
            {
                CompletionStatus = CompletionStatus.UserTerminated,
                UserReason = UserTerminationReason.UserRequested,
                SsqPreTotal = 10f,
                SsqPostTotal = -1f
            };
            Assert.AreEqual(ValidityStatus.IncompleteUserTerminated, v.Evaluate(input));
        }

        [Test]
        public void TechnicalAbort_UsesExistingTechnicalHandling()
        {
            var v = new SessionValidityEvaluator();
            var tracking = new SessionValidityEvaluator.Input
            {
                CompletionStatus = CompletionStatus.UserTerminated,
                UserReason = UserTerminationReason.TrackingLost
            };
            Assert.AreEqual(ValidityStatus.InvalidTrackingFailure, v.Evaluate(tracking));

            var technical = new SessionValidityEvaluator.Input
            {
                CompletionStatus = CompletionStatus.UserTerminated,
                UserReason = UserTerminationReason.TechnicalProblem
            };
            // No dedicated technical status exists; it lands on the user-terminated
            // branch, which is still non-usable and so cannot adapt anything.
            Assert.AreEqual(ValidityStatus.IncompleteUserTerminated, v.Evaluate(technical));
        }

        // ── 5. an abort never changes a level ────────────────────────────────

        [Test]
        public void AbortedStatuses_AreNotUsableByScheduler()
        {
            foreach (var s in new[]
            {
                ValidityStatus.IncompleteUserTerminated,
                ValidityStatus.InvalidSimulatorSickness,
                ValidityStatus.IncompleteTimeExpired,
                ValidityStatus.InvalidTrackingFailure,
                ValidityStatus.InvalidTechnicalFailure
            })
            {
                Assert.IsFalse(UserProfileService.CountsTowardCycle(s),
                    s + " must not count as a usable session");
            }
        }

        [Test]
        public void AbortedSession_DoesNotApplyLevels()
        {
            var p = NewProfile();
            p.currentNBackLevel = 2;
            p.currentPressureLevel = 2;
            _service.CompleteSession(p,
                Finished("s1", ValidityStatus.IncompleteUserTerminated,
                    CompletionStatus.UserTerminated, UserTerminationReason.UserRequested),
                null);                                   // scheduler produced no decision
            Assert.AreEqual(2, p.currentNBackLevel);
            Assert.AreEqual(2, p.currentPressureLevel);
        }

        // ── 5b. the same session may only be applied once ────────────────────

        /// <summary>
        /// CompleteSession is reached from more than one place in the closing flow, and
        /// a repeated save or a re-fired event must not move anything a second time.
        /// The guard keys on sessionId, so one test covers every counter it protects.
        /// </summary>
        [Test]
        public void SameSessionAppliedTwice_ChangesNothingTheSecondTime()
        {
            var p = NewProfile();
            var decision = new AdaptationDecisionData
            {
                sessionId = "repeat-1",
                nBack = new TaskAdaptationDecisionData { newLevel = 2 },
                goNoGo = new TaskAdaptationDecisionData { newLevel = 3 },
                flanker = new TaskAdaptationDecisionData { newLevel = 2 },
                corsi = new TaskAdaptationDecisionData { newLevel = 2 },
                pressure = new PressureAdaptationDecisionData { newLevel = 2 }
            };
            var summary = Finished("repeat-1", ValidityStatus.Valid);

            _service.CompleteSession(p, summary, decision);

            int historyAfterFirst = p.sessionSummaries.Count;
            int indexAfterFirst = p.currentCycleSessionIndex;
            var cycle = p.cycleSummaries.Find(c => c.cycleId == p.activeCycleId);
            int completedAfterFirst = cycle.completedSessionCount;

            // Apply the very same session again, this time with a decision that would
            // push every level further if it were allowed through.
            _service.CompleteSession(p, summary, new AdaptationDecisionData
            {
                sessionId = "repeat-1",
                nBack = new TaskAdaptationDecisionData { newLevel = 3 },
                goNoGo = new TaskAdaptationDecisionData { newLevel = 3 },
                flanker = new TaskAdaptationDecisionData { newLevel = 3 },
                corsi = new TaskAdaptationDecisionData { newLevel = 3 },
                pressure = new PressureAdaptationDecisionData { newLevel = 3 }
            });

            Assert.AreEqual(2, p.currentNBackLevel, "nivo zadatka se ne smije pomjeriti dvaput");
            Assert.AreEqual(3, p.currentGoNoGoLevel);
            Assert.AreEqual(2, p.currentFlankerLevel);
            Assert.AreEqual(2, p.currentCorsiLevel);
            Assert.AreEqual(2, p.currentPressureLevel, "nivo pritiska se ne smije pomjeriti dvaput");
            Assert.AreEqual(indexAfterFirst, p.currentCycleSessionIndex,
                "redni broj sesije u ciklusu se ne smije uvećati dvaput");
            Assert.AreEqual(completedAfterFirst, cycle.completedSessionCount,
                "brojač ciklusa se ne smije uvećati dvaput");
            Assert.AreEqual(historyAfterFirst, p.sessionSummaries.Count,
                "ista sesija se ne smije dvaput upisati u istoriju");
        }

        // ── 6. data is kept ──────────────────────────────────────────────────

        [Test]
        public void AbortedSession_IsStillRecordedInHistory()
        {
            var p = NewProfile();
            var s = Finished("aborted-1", ValidityStatus.InvalidSimulatorSickness,
                CompletionStatus.UserTerminated, UserTerminationReason.SimulatorSickness);
            _service.CompleteSession(p, s, null);

            Assert.AreEqual(1, p.sessionSummaries.Count, "aborted session must be preserved");
            Assert.AreEqual("aborted-1", p.sessionSummaries[0].sessionId);
            Assert.AreEqual(UserTerminationReason.SimulatorSickness,
                p.sessionSummaries[0].userTerminationReason);
            Assert.AreEqual(ValidityStatus.InvalidSimulatorSickness,
                p.sessionSummaries[0].validityStatus);

            var reloaded = _repo.LoadProfile(p.userId);
            Assert.IsNotNull(reloaded);
            Assert.AreEqual(1, reloaded.sessionSummaries.Count, "must survive a reload");
        }

        // ── 10-11. cycle index semantics ─────────────────────────────────────

        [Test]
        public void InvalidOrIncompleteSession_DoesNotConsumeACyclePlace()
        {
            var p = NewProfile();
            int before = p.currentCycleSessionIndex;

            _service.CompleteSession(p, Finished("a", ValidityStatus.IncompleteUserTerminated,
                CompletionStatus.UserTerminated, UserTerminationReason.UserRequested), null);
            _service.CompleteSession(p, Finished("b", ValidityStatus.InvalidSimulatorSickness,
                CompletionStatus.UserTerminated, UserTerminationReason.SimulatorSickness), null);
            _service.CompleteSession(p, Finished("c", ValidityStatus.IncompleteTimeExpired,
                CompletionStatus.SystemTerminated), null);

            Assert.AreEqual(before, p.currentCycleSessionIndex);
            Assert.AreEqual(3, p.sessionSummaries.Count, "history still holds all three");
        }

        [Test]
        public void ValidSession_ConsumesACyclePlace()
        {
            var p = NewProfile();
            int before = p.currentCycleSessionIndex;
            _service.CompleteSession(p, Finished("v1", ValidityStatus.Valid), null);
            Assert.AreEqual(before + 1, p.currentCycleSessionIndex);
            _service.CompleteSession(p, Finished("v2", ValidityStatus.ValidWithWarnings), null);
            Assert.AreEqual(before + 2, p.currentCycleSessionIndex);
        }

        [Test]
        public void CycleReachesFivePlaces_OnlyThroughUsableSessions()
        {
            var p = NewProfile();
            for (int i = 0; i < 3; i++)
                _service.CompleteSession(p, Finished("bad" + i,
                    ValidityStatus.IncompleteUserTerminated,
                    CompletionStatus.UserTerminated, UserTerminationReason.UserRequested), null);
            for (int i = 0; i < 5; i++)
                _service.CompleteSession(p, Finished("ok" + i, ValidityStatus.Valid), null);

            Assert.AreEqual(5, p.currentCycleSessionIndex);
            Assert.AreEqual(8, p.sessionSummaries.Count);
            var cycle = p.cycleSummaries.Find(c => c.cycleId == p.activeCycleId);
            Assert.IsNotNull(cycle);
            Assert.AreEqual(5, cycle.completedSessionCount);
        }

        // ── cycle summary repair ─────────────────────────────────────────────
        // CreateProfile assigns an activeCycleId without writing its summary record,
        // which silently discarded the cycle-level STAI scores. Verified against a real
        // device profile: cycleSummaries was empty after five sessions.

        [Test]
        public void NewProfile_GetsACycleSummaryItCanWriteInto()
        {
            var p = NewProfile();
            var cycle = p.cycleSummaries.Find(c => c.cycleId == p.activeCycleId);
            Assert.IsNotNull(cycle, "an active cycle must have a record to store into");
            Assert.AreEqual(p.plannedCycleSessionCount, cycle.plannedSessionCount);
            Assert.AreEqual(0, cycle.completedSessionCount);
        }

        [Test]
        public void ProfileWithOrphanedCycleId_IsRepairedAndKeepsItsPlace()
        {
            var p = _service.CreateProfile("zatecen");
            p.cycleSummaries.Clear();                       // the state seen on the device
            p.activeCycleId = "orphan-cycle";
            p.currentCycleSessionIndex = 2;
            p.sessionSummaries.Add(Finished("s1", ValidityStatus.Valid));
            p.sessionSummaries[0].cycleId = "orphan-cycle";
            p.sessionSummaries.Add(Finished("s2", ValidityStatus.ValidWithWarnings));
            p.sessionSummaries[1].cycleId = "orphan-cycle";
            p.sessionSummaries.Add(Finished("s3", ValidityStatus.IncompleteUserTerminated,
                CompletionStatus.UserTerminated, UserTerminationReason.UserRequested));
            p.sessionSummaries[2].cycleId = "orphan-cycle";

            _service.EnsureActiveCycle(p);

            var cycle = p.cycleSummaries.Find(c => c.cycleId == "orphan-cycle");
            Assert.IsNotNull(cycle, "the missing record must be created, not a new cycle");
            Assert.AreEqual("orphan-cycle", p.activeCycleId, "the cycle must not be restarted");
            Assert.AreEqual(2, cycle.completedSessionCount,
                "only the two usable sessions are counted back");
        }

        [Test]
        public void RepairedCycle_CanStoreTheStaiScore()
        {
            var p = NewProfile();
            var cycle = p.cycleSummaries.Find(c => c.cycleId == p.activeCycleId);
            Assert.IsNotNull(cycle);
            cycle.staiScoreCycleStart = 44f;                // what the flow does on CycleStart
            Assert.IsFalse(UserProfileService.ShouldAdministerCycleStartStai(p),
                "a stored score must stop the opening STAI being asked twice");
        }

        // ── 12. STAI start/end still behave ──────────────────────────────────

        [Test]
        public void AbortBeforeStai_LeavesCycleStartStaiStillDue()
        {
            var p = NewProfile();
            Assert.IsTrue(UserProfileService.ShouldAdministerCycleStartStai(p));
            _service.CompleteSession(p, Finished("a", ValidityStatus.IncompleteUserTerminated,
                CompletionStatus.UserTerminated, UserTerminationReason.UserRequested), null);
            Assert.IsTrue(UserProfileService.ShouldAdministerCycleStartStai(p),
                "the cycle never started, so its opening STAI is still due");
        }

        [Test]
        public void StaiAlreadyRecorded_IsNotAskedAgainAfterAnAbort()
        {
            var p = NewProfile();
            var cycle = p.cycleSummaries.Find(c => c.cycleId == p.activeCycleId);
            cycle.staiScoreCycleStart = 41f;                 // answered, then aborted
            _service.CompleteSession(p, Finished("a", ValidityStatus.IncompleteUserTerminated,
                CompletionStatus.UserTerminated, UserTerminationReason.UserRequested), null);
            Assert.IsFalse(UserProfileService.ShouldAdministerCycleStartStai(p),
                "a recorded score must not be collected twice");
        }

        [Test]
        public void CycleEndStai_BecomesDueOnlyAfterUsableSessionsFillTheCycle()
        {
            var p = NewProfile();
            for (int i = 0; i < 4; i++)
                _service.CompleteSession(p, Finished("ok" + i, ValidityStatus.Valid), null);
            Assert.IsTrue(UserProfileService.IsLastSessionOfCycle(p),
                "the fifth session of five is the last one");

            var fresh = NewProfile();
            for (int i = 0; i < 4; i++)
                _service.CompleteSession(fresh, Finished("bad" + i,
                    ValidityStatus.IncompleteUserTerminated,
                    CompletionStatus.UserTerminated, UserTerminationReason.UserRequested), null);
            Assert.IsFalse(UserProfileService.IsLastSessionOfCycle(fresh),
                "four aborted attempts must not push the cycle to its end");
        }
    }
}
