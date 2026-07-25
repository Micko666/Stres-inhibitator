using NUnit.Framework;
using StressTraining.Core;

namespace StressTraining.Tests.EditMode
{
    public sealed class SessionClockTests
    {
        [Test]
        public void ActiveAndPausedTime_AreSeparated()
        {
            var clock = new SessionClock();
            clock.StartSession();
            clock.Tick(2.0);
            clock.SetPaused(true);
            clock.Tick(3.0);
            clock.SetPaused(false);
            clock.Tick(1.0);

            Assert.That(clock.RealElapsedSeconds, Is.EqualTo(6.0).Within(0.0001));
            Assert.That(clock.ActiveElapsedSeconds, Is.EqualTo(3.0).Within(0.0001));
            Assert.That(clock.PausedElapsedSeconds, Is.EqualTo(3.0).Within(0.0001));
            Assert.That(clock.PauseCount, Is.EqualTo(1));
        }

        [Test]
        public void ArmedBudget_DoesNotRunDuringInstructionCountdownOrTransition()
        {
            var clock = new SessionClock();
            clock.StartSession();
            clock.SetGlobalDuration(100.0);

            Assert.That(clock.GlobalClockState, Is.EqualTo(GlobalChallengeClockState.Ready));
            clock.Tick(25.0);
            Assert.That(clock.GlobalElapsedSeconds, Is.Zero);
            Assert.That(clock.RemainingGlobalSeconds, Is.EqualTo(100.0).Within(0.0001));

            clock.SetGlobalChallengeRunning(false);
            clock.Tick(10.0);
            Assert.That(clock.GlobalClockState, Is.EqualTo(GlobalChallengeClockState.Ready));
            Assert.That(clock.GlobalChallengeStarted, Is.False);
            Assert.That(clock.RemainingGlobalSeconds, Is.EqualTo(100.0).Within(0.0001));
        }

        [Test]
        public void ActiveChallengeGate_ConsumesOnlyOpenIntervals()
        {
            var clock = new SessionClock();
            clock.StartSession();
            clock.SetGlobalDuration(100.0);

            clock.SetGlobalChallengeRunning(true);
            clock.Tick(12.0); // active trial / Corsi presentation or response
            clock.SetGlobalChallengeRunning(false);
            clock.Tick(30.0); // task transition
            clock.SetGlobalChallengeRunning(true);
            clock.Tick(8.0);

            Assert.That(clock.GlobalElapsedSeconds, Is.EqualTo(20.0).Within(0.0001));
            Assert.That(clock.RemainingGlobalSeconds, Is.EqualTo(80.0).Within(0.0001));
        }

        [Test]
        public void ManualPause_FreezesRunningChallengeClock()
        {
            var clock = new SessionClock();
            clock.StartSession();
            clock.SetGlobalDuration(60.0);
            clock.SetGlobalChallengeRunning(true);
            clock.Tick(10.0);

            clock.SetPaused(true);
            clock.Tick(20.0);
            Assert.That(clock.IsGlobalChallengePaused, Is.True);
            Assert.That(clock.RemainingGlobalSeconds, Is.EqualTo(50.0).Within(0.0001));

            clock.SetPaused(false);
            clock.Tick(50.0);
            Assert.That(clock.GlobalTimeExpired, Is.True);
            Assert.That(clock.GlobalClockState, Is.EqualTo(GlobalChallengeClockState.Expired));
        }

        [Test]
        public void StopBeforeZero_RepresentsCompletedPlanNotExpiry()
        {
            var clock = new SessionClock();
            clock.StartSession();
            clock.SetGlobalDuration(30.0);
            clock.SetGlobalChallengeRunning(true);
            clock.Tick(20.0);
            clock.StopGlobalChallenge();

            Assert.That(clock.GlobalTimeExpired, Is.False);
            Assert.That(clock.RemainingGlobalSeconds, Is.EqualTo(10.0).Within(0.0001));
            Assert.That(clock.GlobalClockState, Is.EqualTo(GlobalChallengeClockState.Stopped));
        }

        [Test]
        public void ZeroSeconds_TransitionsToExpired()
        {
            var clock = new SessionClock();
            clock.StartSession();
            clock.SetGlobalDuration(5.0);
            clock.SetGlobalChallengeRunning(true);
            clock.Tick(5.0);

            Assert.That(clock.RemainingGlobalSeconds, Is.Zero);
            Assert.That(clock.GlobalTimeExpired, Is.True);
            Assert.That(clock.GlobalClockState, Is.EqualTo(GlobalChallengeClockState.Expired));
        }
    }
}
