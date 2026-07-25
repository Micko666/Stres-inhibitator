using System;
using UnityEngine;

namespace StressTraining.Persistence
{
    /// <summary>
    /// Versioned-JSON migration point. Every persisted record carries schemaVersion.
    /// Schema upgrades are additive; old files remain readable and newer unknown
    /// schemas are never downgraded.
    /// </summary>
    public sealed class SchemaMigrationService
    {
        public const int CurrentProfileSchema = Data.UserProfileData.CurrentSchemaVersion;
        public const int CurrentSessionSchema = Data.SessionSummaryData.CurrentSchemaVersion;

        [Serializable]
        private sealed class SchemaProbe { public int schemaVersion; }

        public static int ReadSchemaVersion(string json)
        {
            try
            {
                var probe = JsonUtility.FromJson<SchemaProbe>(json);
                return probe?.schemaVersion ?? 0;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Returns migrated JSON (or the input unchanged when already current).
        /// Returns null when the payload is from an UNKNOWN NEWER schema —
        /// newer files are never destructively downgraded.
        /// </summary>
        public string MigrateProfileJson(string json)
        {
            int v = ReadSchemaVersion(json);
            if (v == CurrentProfileSchema) return json;
            if (v > CurrentProfileSchema) return null;

            // v0/v1 → v2 (Corsi pool): JsonUtility fills every missing field with
            // its declared default — but ONLY when the default lives in the field
            // initializer of a fresh instance. FromJson overwrites listed fields
            // and keeps initializer values for absent ones, so:
            //   currentCorsiLevel  → 1 (initializer)
            //   tutorialStates     → empty list
            // We then normalize schemaVersion + guard the level range explicitly
            // so a v1 profile can never surface with Corsi level 0.
            try
            {
                var profile = JsonUtility.FromJson<Data.UserProfileData>(json);
                if (profile == null) return null;
                if (profile.currentCorsiLevel < 1) profile.currentCorsiLevel = 1;
                if (profile.tutorialStates == null)
                    profile.tutorialStates = new System.Collections.Generic.List<Data.TaskTutorialStateData>();
                profile.schemaVersion = CurrentProfileSchema;
                return JsonUtility.ToJson(profile, true);
            }
            catch
            {
                return null;
            }
        }

        public string MigrateSessionJson(string json)
        {
            int v = ReadSchemaVersion(json);
            if (v == CurrentSessionSchema) return json;
            if (v > CurrentSessionSchema) return null;

            // v0/v1/v2/v3 → current: additive fields only. Historical 15-block
            // production records keep plannedRoundCount=0 because they did not
            // use the Phase 8.5 three-round contract; current sessions set it
            // explicitly when their summary is created.
            try
            {
                var summary = JsonUtility.FromJson<Data.SessionSummaryData>(json);
                if (summary == null) return null;
                if (v < 3) summary.plannedRoundCount = 0;
                if (v < 4 && summary.globalDifficultyTimeMultiplier <= 0f)
                    summary.globalDifficultyTimeMultiplier = 1f;
                summary.schemaVersion = CurrentSessionSchema;
                return JsonUtility.ToJson(summary, true);
            }
            catch
            {
                return null;
            }
        }
    }
}
