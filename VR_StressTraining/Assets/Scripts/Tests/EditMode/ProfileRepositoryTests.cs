using System.IO;
using NUnit.Framework;
using StressTraining.Core;
using StressTraining.Persistence;

namespace StressTraining.Tests.EditMode
{
    public sealed class ProfileRepositoryTests
    {
        private string _temp;
        private PersistencePaths _paths;
        private ProfileRepository _repository;

        [SetUp]
        public void SetUp()
        {
            _temp = Path.Combine(Path.GetTempPath(), "StressTrainingTests", System.Guid.NewGuid().ToString("N"));
            _paths = new PersistencePaths(_temp);
            _repository = new ProfileRepository(_paths, new SchemaMigrationService(),
                new BackupRecoveryService(_paths), new AppErrorService());
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_temp)) Directory.Delete(_temp, true);
        }

        [Test]
        public void UserId_IsStableAcrossSaveAndLoad()
        {
            var created = _repository.CreateProfile("Mihailo", 48, 5);
            string id = created.userId;
            created.username = "Mihailo D";
            _repository.SaveProfile(created);

            Assert.That(_repository.LoadProfile(id).userId, Is.EqualTo(id));
        }

        [Test]
        public void CorruptMainJson_RecoversPreviousValidProfile()
        {
            var profile = _repository.CreateProfile("Recovery User", 48, 5);
            profile.username = "Valid Previous";
            _repository.SaveProfile(profile);
            profile.username = "Newest Valid";
            _repository.SaveProfile(profile);
            File.WriteAllText(_paths.ProfileFile(profile.userId), "{broken-json");

            var recovered = _repository.LoadProfile(profile.userId);
            Assert.That(recovered, Is.Not.Null);
            Assert.That(recovered.userId, Is.EqualTo(profile.userId));
        }
    }
}
