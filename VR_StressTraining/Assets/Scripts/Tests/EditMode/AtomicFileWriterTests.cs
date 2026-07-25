using System.IO;
using NUnit.Framework;
using StressTraining.Persistence;

namespace StressTraining.Tests.EditMode
{
    public sealed class AtomicFileWriterTests
    {
        private string _dir;
        private string _file;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "StressTrainingAtomic", System.Guid.NewGuid().ToString("N"));
            _file = Path.Combine(_dir, "value.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void SecondWrite_PreservesPreviousValidCopy()
        {
            AtomicFileWriter.Write(_file, "first");
            AtomicFileWriter.Write(_file, "second");

            Assert.That(File.ReadAllText(_file), Is.EqualTo("second"));
            Assert.That(File.ReadAllText(AtomicFileWriter.PrevPath(_file)), Is.EqualTo("first"));
        }
    }
}
