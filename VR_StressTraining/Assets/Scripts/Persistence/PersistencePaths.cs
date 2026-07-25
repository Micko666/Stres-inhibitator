using System.IO;

namespace StressTraining.Persistence
{
    /// <summary>
    /// Single source of truth for the on-disk layout (spec §6):
    ///
    /// {root}/StressTrainingData/
    /// ├─ profiles/
    /// │  ├─ index.json
    /// │  ├─ &lt;userId&gt;.profile.json
    /// │  └─ backups/
    /// ├─ sessions/&lt;userId&gt;/&lt;sessionId&gt;/{session.json, trials.jsonl, hr.jsonl, events.jsonl, questionnaires.json}
    /// ├─ config/
    /// └─ logs/
    ///
    /// The root is injectable so tests can use a temp directory. Runtime code passes
    /// Application.persistentDataPath.
    /// </summary>
    public sealed class PersistencePaths
    {
        public string Root { get; }

        public PersistencePaths(string baseDir)
        {
            Root = Path.Combine(baseDir, "StressTrainingData");
        }

        public string ProfilesDir => Path.Combine(Root, "profiles");
        public string ProfileBackupsDir => Path.Combine(ProfilesDir, "backups");
        public string IndexFile => Path.Combine(ProfilesDir, "index.json");
        public string ProfileFile(string userId) => Path.Combine(ProfilesDir, userId + ".profile.json");

        public string SessionsDir => Path.Combine(Root, "sessions");
        public string UserSessionsDir(string userId) => Path.Combine(SessionsDir, userId);
        public string SessionDir(string userId, string sessionId) =>
            Path.Combine(SessionsDir, userId, sessionId);

        public string SessionFile(string userId, string sessionId) =>
            Path.Combine(SessionDir(userId, sessionId), "session.json");
        public string TrialsFile(string userId, string sessionId) =>
            Path.Combine(SessionDir(userId, sessionId), "trials.jsonl");
        public string HrFile(string userId, string sessionId) =>
            Path.Combine(SessionDir(userId, sessionId), "hr.jsonl");
        public string EventsFile(string userId, string sessionId) =>
            Path.Combine(SessionDir(userId, sessionId), "events.jsonl");
        public string QuestionnairesFile(string userId, string sessionId) =>
            Path.Combine(SessionDir(userId, sessionId), "questionnaires.json");

        public string ConfigDir => Path.Combine(Root, "config");
        public string LogsDir => Path.Combine(Root, "logs");

        public void EnsureBaseDirectories()
        {
            Directory.CreateDirectory(ProfilesDir);
            Directory.CreateDirectory(ProfileBackupsDir);
            Directory.CreateDirectory(SessionsDir);
            Directory.CreateDirectory(ConfigDir);
            Directory.CreateDirectory(LogsDir);
        }
    }
}
