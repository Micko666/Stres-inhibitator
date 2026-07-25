using NUnit.Framework;
using StressTraining.Core;

namespace StressTraining.Tests.EditMode
{
    public sealed class UsernameValidatorTests
    {
        [Test]
        public void EmptyUsername_IsRejected()
        {
            Assert.That(UsernameValidator.Validate("   ", null), Is.EqualTo(UsernameValidationResult.Empty));
        }

        [Test]
        public void Duplicate_IsCaseInsensitive()
        {
            Assert.That(UsernameValidator.Validate("mIhAiLo", new[] { "Mihailo" }),
                Is.EqualTo(UsernameValidationResult.DuplicateCaseInsensitive));
        }
    }
}
