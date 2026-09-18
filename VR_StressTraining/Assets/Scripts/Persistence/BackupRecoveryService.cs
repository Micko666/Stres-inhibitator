using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace StressTraining.Persistence
{
    /// <summary>
    /// Rolling profile backups + recovery from corruption.
    /// On every profile save a timestamped copy goes to profiles/backups/;
    /// the newest 5 per user are kept. Recovery order for a corrupted profile:
    /// 1. "&lt;file&gt;.prev" (last valid copy from AtomicFileWriter)
    /// 2. newest parseable timestamped backup
    /// A corrupted JSON never crashes the app (spec §6); the caller shows a
    /// controlled error and this service is invoked from the developer tools.
    /// </summary>
    public sealed class BackupRecoveryService
    {
        public const int MaxBackupsPerUser = 5;

        private readonly PersistencePaths _paths;

        public BackupRecoveryService(PersistencePaths paths)
        {
            _paths = paths;
        }

        public void BackupProfile(string userId, string profileJson)
        {
            try
            {
                Directory.CreateDirectory(_paths.ProfileBackupsDir);
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                string file = Path.Combine(_paths.ProfileBackupsDir, $"{userId}.{stamp}.json");
                File.WriteAllText(file, profileJson);
                Prune(userId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BackupRecovery] Backup failed for {userId}: {ex.Message}");
            }
        }

        private void Prune(string userId)
        {
            var files = ListBackups(userId);
            for (int i = MaxBackupsPerUser; i < files.Count; i++)
            {
                try { File.Delete(files[i]); } catch { /* best effort */ }
            }
        }

        /// <summary>Backups newest-first.</summary>
        public List<string> ListBackups(string userId)
        {
            try
            {
                if (!Directory.Exists(_paths.ProfileBackupsDir)) return new List<string>();
                return Directory.GetFiles(_paths.ProfileBackupsDir, userId + ".*.json")
                    .OrderByDescending(f => f, StringComparer.Ordinal)
                    .ToList();
            }
            catch { return new List<string>(); }
        }

        /// <summary>
        /// Attempts to recover a valid profile JSON for the user.
        /// <paramref name="isValid"/> decides whether a candidate parses correctly.
        /// Returns null when nothing recoverable exists.
        /// </summary>
        public string TryRecoverProfileJson(string userId, Func<string, bool> isValid)
        {
            string prev = AtomicFileWriter.PrevPath(_paths.ProfileFile(userId));
            string candidate = TryRead(prev);
            if (candidate != null && isValid(candidate)) return candidate;

            foreach (var backup in ListBackups(userId))
            {
                candidate = TryRead(backup);
                if (candidate != null && isValid(candidate)) return candidate;
            }
            return null;
        }

        private static string TryRead(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch { return null; }
        }
    }
}
