using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StressTraining.Core;
using StressTraining.Data;
using UnityEngine;

namespace StressTraining.Persistence
{
    /// <summary>
    /// Session directory lifecycle + session.json persistence.
    /// Data from unfinished sessions is preserved for audit (spec §6):
    /// on boot, sessions without an end timestamp are marked Abandoned, never deleted.
    /// </summary>
    public sealed class SessionRepository
    {
        private readonly PersistencePaths _paths;
        private readonly SchemaMigrationService _migration;
        private readonly AppErrorService _errors;

        public SessionRepository(PersistencePaths paths, SchemaMigrationService migration, AppErrorService errors)
        {
            _paths = paths;
            _migration = migration;
            _errors = errors;
        }

        public string CreateSessionDirectory(string userId, string sessionId)
        {
            string dir = _paths.SessionDir(userId, sessionId);
            Directory.CreateDirectory(dir);
            return dir;
        }

        public void SaveSessionSummary(SessionSummaryData summary)
        {
            string file = _paths.SessionFile(summary.userId, summary.sessionId);
            AtomicFileWriter.Write(file, JsonUtility.ToJson(summary, true));
        }

        public SessionSummaryData LoadSessionSummary(string userId, string sessionId)
        {
            try
            {
                string file = _paths.SessionFile(userId, sessionId);
                if (!File.Exists(file)) return null;
                string json = _migration?.MigrateSessionJson(File.ReadAllText(file));
                return json == null ? null : JsonUtility.FromJson<SessionSummaryData>(json);
            }
            catch (Exception ex)
            {
                _errors?.Report("SESSION_LOAD_FAILED", "Zapis sesije nije čitljiv.",
                    $"{userId}/{sessionId}: {ex.Message}", true, ErrorSessionImpact.None, ex);
                return null;
            }
        }

        public List<string> ListSessionIds(string userId)
        {
            try
            {
                string dir = _paths.UserSessionsDir(userId);
                if (!Directory.Exists(dir)) return new List<string>();
                return Directory.GetDirectories(dir).Select(Path.GetFileName).OrderBy(s => s).ToList();
            }
            catch { return new List<string>(); }
        }

        /// <summary>
        /// Called at boot: any session whose summary has no end timestamp was
        /// interrupted by a crash/app kill. It is flagged Abandoned + preserved.
        /// Returns the number of sessions flagged.
        /// </summary>
        public int DetectAndMarkAbandonedSessions(string userId)
        {
            int flagged = 0;
            foreach (var sessionId in ListSessionIds(userId))
            {
                var summary = LoadSessionSummary(userId, sessionId);
                if (summary == null) continue;
                if (!string.IsNullOrEmpty(summary.endedAtUtcIso)) continue;
                if (summary.completionStatus != CompletionStatus.Unknown) continue;

                summary.completionStatus = CompletionStatus.Abandoned;
                summary.endedAtUtcIso = UtcTime.NowIso();
                summary.validityStatus = ValidityStatus.InvalidTechnicalFailure;
                summary.validityNotes.Add("Marked Abandoned at boot: session had no end timestamp.");
                SaveSessionSummary(summary);
                flagged++;
            }
            return flagged;
        }
    }
}
