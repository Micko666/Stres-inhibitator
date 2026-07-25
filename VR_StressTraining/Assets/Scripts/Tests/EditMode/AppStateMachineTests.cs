using NUnit.Framework;
using StressTraining.Core;

namespace StressTraining.Tests.EditMode
{
    public sealed class AppStateMachineTests
    {
        [Test]
        public void Boot_ToProfileSelection_IsValid()
        {
            var machine = new AppStateMachine();
            Assert.That(machine.RequestTransition(AppState.ProfileSelection, "test"), Is.True);
            Assert.That(machine.Current, Is.EqualTo(AppState.ProfileSelection));
        }

        [Test]
        public void Boot_ToActiveSession_IsRejected()
        {
            var machine = new AppStateMachine();
            Assert.That(machine.RequestTransition(AppState.ActiveSession, "invalid"), Is.False);
            Assert.That(machine.Current, Is.EqualTo(AppState.Boot));
        }

        [Test]
        public void DemoOverview_BackToProfiles_IsValidatedTransition()
        {
            var machine = new AppStateMachine();
            Assert.That(machine.RequestTransition(AppState.ProfileSelection, "boot"), Is.True);
            Assert.That(machine.RequestTransition(AppState.Preparation, "profile_selected"), Is.True);
            Assert.That(machine.RequestTransition(AppState.ProfileSelection, "back"), Is.True);
            Assert.That(machine.Current, Is.EqualTo(AppState.ProfileSelection));
            Assert.That(machine.History[machine.History.Count - 1].Forced, Is.False);
        }

        [Test]
        public void DemoReady_CanOpenActiveSafeSpaceTutorialWithoutCorridorTransition()
        {
            var machine = new AppStateMachine();
            Assert.That(machine.RequestTransition(AppState.ProfileSelection, "boot"), Is.True);
            Assert.That(machine.RequestTransition(AppState.Preparation, "profile"), Is.True);
            Assert.That(machine.RequestTransition(AppState.Ready, "demo_created"), Is.True);
            Assert.That(machine.RequestTransition(AppState.ActiveSession, "tutorial_ready"), Is.True);
            Assert.That(machine.Current, Is.EqualTo(AppState.ActiveSession));
        }
    }
}
