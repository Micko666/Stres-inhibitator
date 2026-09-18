using System;
using System.Collections.Generic;

namespace StressTraining.Data
{
    /// <summary>
    /// Per-user persistent profile. Stored as profiles/&lt;userId&gt;.profile.json.
    /// userId is a stable GUID and the primary key; username is display-only and
    /// may be renamed without changing userId.
    /// All DateTime values are stored as UTC ISO-8601 strings; empty string = null.
    /// </summary>
    [Serializable]
    public sealed class UserProfileData
    {
        public const int CurrentSchemaVersion = 2;

        // v1 → v2: added currentCorsiLevel and tutorialStates (Corsi task pool +
        // per-task tutorial repetition policy). Migration in SchemaMigrationService.
        public int schemaVersion = CurrentSchemaVersion;

        public string userId;                       // stable GUID, never changes
        public string username;                     // display name, unique case-insensitively
        public string createdAtUtcIso;
        public string lastSessionAtUtcIso = "";     // "" = never
        public string nextRecommendedSessionAtUtcIso = "";
        public int sessionIntervalHours = 48;       // per-profile; default from SessionConfig

        public string activeCycleId = "";
        public int currentCycleSessionIndex;        // 0-based index of NEXT session in cycle
        public int plannedCycleSessionCount = 5;

        // Local difficulty levels (1..3). Changed ONLY between sessions by the scheduler.
        public int currentNBackLevel = 1;
        public int currentGoNoGoLevel = 1;
        public int currentFlankerLevel = 1;
        public int currentCorsiLevel = 1;           // v2 — Corsi-inspired task level
        public int currentPressureLevel = 1;        // global pressure level (1..3)

        // v2 — per-task tutorial repetition policy (spec §14).
        public List<TaskTutorialStateData> tutorialStates = new List<TaskTutorialStateData>();

        public List<TrainingCycleSummaryData> cycleSummaries = new List<TrainingCycleSummaryData>();
        public List<SessionSummaryData> sessionSummaries = new List<SessionSummaryData>();
        public UserSettingsData settings = new UserSettingsData();

        public int GetTaskLevel(TaskType task)
        {
            switch (task)
            {
                case TaskType.NBack: return currentNBackLevel;
                case TaskType.GoNoGo: return currentGoNoGoLevel;
                case TaskType.Flanker: return currentFlankerLevel;
                case TaskType.CorsiSequence: return currentCorsiLevel;
                default: return 1;
            }
        }

        public TaskTutorialStateData GetOrCreateTutorialState(TaskType task)
        {
            var state = tutorialStates.Find(s => s.taskType == task);
            if (state == null)
            {
                state = new TaskTutorialStateData { taskType = task };
                tutorialStates.Add(state);
            }
            return state;
        }
    }

    /// <summary>Per-task tutorial completion tracking (spec §14).</summary>
    [Serializable]
    public sealed class TaskTutorialStateData
    {
        public TaskType taskType;
        public bool taskTutorialCompleted;
        public int taskTutorialVersion;             // version of rules the user last saw
        public string lastTutorialAtUtcIso = "";
    }

    [Serializable]
    public sealed class UserSettingsData
    {
        public float uiScale = 1f;
        public float voiceVolume = 1f;
        public float ambientVolume = 0.8f;
        public bool subtitlesEnabled = true;
        public string preferredLanguage = "bcs";
    }

    [Serializable]
    public sealed class TrainingCycleSummaryData
    {
        public string cycleId;
        public string startedAtUtcIso;
        public string endedAtUtcIso = "";
        public int completedSessionCount;
        public int plannedSessionCount;
        public float staiScoreCycleStart = -1f;     // -1 = not administered
        public float staiScoreCycleEnd = -1f;
    }

    /// <summary>Lightweight list stored in profiles/index.json for fast profile selection UI.</summary>
    [Serializable]
    public sealed class ProfileIndexData
    {
        public int schemaVersion = 1;
        public List<ProfileIndexEntry> profiles = new List<ProfileIndexEntry>();
    }

    [Serializable]
    public sealed class ProfileIndexEntry
    {
        public string userId;
        public string username;
        public string createdAtUtcIso;
        public string lastSessionAtUtcIso = "";
        public string nextRecommendedSessionAtUtcIso = "";
    }
}
