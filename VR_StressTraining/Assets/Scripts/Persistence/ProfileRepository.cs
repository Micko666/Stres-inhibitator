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
    /// Load/save of user profiles and the fast-selection index.
    /// - Atomic writes with .prev copies (AtomicFileWriter)
    /// - Rolling timestamped backups (BackupRecoveryService)
    /// - Corrupted JSON is recovered when possible, reported otherwise — never a crash
    /// - Index is rebuilt from profile files when index.json itself is corrupted
    /// </summary>
    public sealed class ProfileRepository
    {
        private readonly PersistencePaths _paths;
        private readonly SchemaMigrationService _migration;
        private readonly BackupRecoveryService _backup;
        private readonly AppErrorService _errors;

        public ProfileRepository(PersistencePaths paths, SchemaMigrationService migration,
            BackupRecoveryService backup, AppErrorService errors)
        {
            _paths = paths;
            _migration = migration;
            _backup = backup;
            _errors = errors;
            _paths.EnsureBaseDirectories();
        }

        // ── Index ────────────────────────────────────────────────────────

        public ProfileIndexData LoadIndex()
        {
            try
            {
                if (File.Exists(_paths.IndexFile))
                {
                    var index = JsonUtility.FromJson<ProfileIndexData>(File.ReadAllText(_paths.IndexFile));
                    if (index != null && index.profiles != null) return index;
                }
            }
            catch (Exception ex)
            {
                _errors?.Report("PROFILE_INDEX_CORRUPT",
                    "Lista profila je oštećena — obnavljam iz pojedinačnih profila.",
                    $"index.json unreadable: {ex.Message}", true, ErrorSessionImpact.None, ex);
            }
            return RebuildIndex();
        }

        public ProfileIndexData RebuildIndex()
        {
            var index = new ProfileIndexData();
            try
            {
                if (Directory.Exists(_paths.ProfilesDir))
                {
                    foreach (var file in Directory.GetFiles(_paths.ProfilesDir, "*.profile.json"))
                    {
                        var profile = LoadProfileFromFile(file);
                        if (profile != null) index.profiles.Add(ToIndexEntry(profile));
                    }
                }
                SaveIndex(index);
            }
            catch (Exception ex)
            {
                _errors?.Report("PROFILE_INDEX_REBUILD_FAILED", "Obnova liste profila nije uspjela.",
                    ex.Message, true, ErrorSessionImpact.None, ex);
            }
            return index;
        }

        private void SaveIndex(ProfileIndexData index)
        {
            AtomicFileWriter.Write(_paths.IndexFile, JsonUtility.ToJson(index, true));
        }

        private static ProfileIndexEntry ToIndexEntry(UserProfileData p) => new ProfileIndexEntry
        {
            userId = p.userId,
            username = p.username,
            createdAtUtcIso = p.createdAtUtcIso,
            lastSessionAtUtcIso = p.lastSessionAtUtcIso,
            nextRecommendedSessionAtUtcIso = p.nextRecommendedSessionAtUtcIso
        };

        /// <summary>Sorted for the selection UI: most recently used first, then by name.</summary>
        public List<ProfileIndexEntry> ListProfiles()
        {
            var index = LoadIndex();
            return index.profiles
                .OrderByDescending(p => p.lastSessionAtUtcIso ?? "", StringComparer.Ordinal)
                .ThenBy(p => p.username, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // ── Create / rename ──────────────────────────────────────────────

        public UsernameValidationResult ValidateNewUsername(string candidate)
        {
            var existing = LoadIndex().profiles.Select(p => p.username);
            return UsernameValidator.Validate(candidate, existing);
        }

        /// <summary>Creates and persists a new profile. Caller must have validated the username.</summary>
        public UserProfileData CreateProfile(string username, int defaultIntervalHours, int plannedCycleSessions)
        {
            var profile = new UserProfileData
            {
                userId = Guid.NewGuid().ToString("N"),
                username = UsernameValidator.Normalize(username),
                createdAtUtcIso = UtcTime.NowIso(),
                sessionIntervalHours = defaultIntervalHours,
                plannedCycleSessionCount = plannedCycleSessions,
                activeCycleId = Guid.NewGuid().ToString("N"),
                currentCycleSessionIndex = 0
            };
            SaveProfile(profile);
            return profile;
        }

        /// <summary>Renaming never changes userId (spec §5).</summary>
        public bool RenameProfile(string userId, string newUsername)
        {
            var profile = LoadProfile(userId);
            if (profile == null) return false;
            var others = LoadIndex().profiles.Where(p => p.userId != userId).Select(p => p.username);
            if (UsernameValidator.Validate(newUsername, others) != UsernameValidationResult.Ok) return false;
            profile.username = UsernameValidator.Normalize(newUsername);
            SaveProfile(profile);
            return true;
        }

        // ── Save / load ──────────────────────────────────────────────────

        public void SaveProfile(UserProfileData profile)
        {
            string json = JsonUtility.ToJson(profile, true);
            AtomicFileWriter.Write(_paths.ProfileFile(profile.userId), json);
            _backup?.BackupProfile(profile.userId, json);

            var index = LoadIndexNoRecovery() ?? new ProfileIndexData();
            index.profiles.RemoveAll(p => p.userId == profile.userId);
            index.profiles.Add(ToIndexEntry(profile));
            SaveIndex(index);
        }

        private ProfileIndexData LoadIndexNoRecovery()
        {
            try
            {
                if (!File.Exists(_paths.IndexFile)) return null;
                return JsonUtility.FromJson<ProfileIndexData>(File.ReadAllText(_paths.IndexFile));
            }
            catch { return null; }
        }

        public UserProfileData LoadProfile(string userId)
        {
            string file = _paths.ProfileFile(userId);
            var profile = LoadProfileFromFile(file);
            if (profile != null) return profile;

            // Corrupted or missing — attempt recovery from .prev / backups.
            string recovered = _backup?.TryRecoverProfileJson(userId, IsParseableProfile);
            if (recovered != null)
            {
                _errors?.Report("PROFILE_RECOVERED",
                    "Profil je obnovljen iz rezervne kopije.",
                    $"Profile {userId} restored from backup.", true, ErrorSessionImpact.None);
                var restored = ParseProfile(recovered);
                if (restored != null)
                {
                    AtomicFileWriter.Write(file, recovered);
                    return restored;
                }
            }

            if (File.Exists(file))
            {
                _errors?.Report("PROFILE_CORRUPT",
                    "Profil je oštećen i nije mogao biti obnovljen.",
                    $"Profile {userId} unreadable and no valid backup found.",
                    true, ErrorSessionImpact.None);
            }
            return null;
        }

        private UserProfileData LoadProfileFromFile(string file)
        {
            try
            {
                if (!File.Exists(file)) return null;
                return ParseProfile(File.ReadAllText(file));
            }
            catch { return null; }
        }

        private UserProfileData ParseProfile(string json)
        {
            try
            {
                string migrated = _migration?.MigrateProfileJson(json) ?? json;
                if (migrated == null) return null; // newer schema than this build understands
                var p = JsonUtility.FromJson<UserProfileData>(migrated);
                return string.IsNullOrEmpty(p?.userId) ? null : p;
            }
            catch { return null; }
        }

        private bool IsParseableProfile(string json) => ParseProfile(json) != null;

        /// <summary>Developer-tool only: removes profile + index entry (sessions stay for audit).</summary>
        public void DeleteProfile(string userId)
        {
            try
            {
                string file = _paths.ProfileFile(userId);
                if (File.Exists(file)) File.Delete(file);
                string prev = AtomicFileWriter.PrevPath(file);
                if (File.Exists(prev)) File.Delete(prev);
                var index = LoadIndexNoRecovery();
                if (index != null)
                {
                    index.profiles.RemoveAll(p => p.userId == userId);
                    SaveIndex(index);
                }
            }
            catch (Exception ex)
            {
                _errors?.Report("PROFILE_DELETE_FAILED", "Brisanje profila nije uspjelo.",
                    ex.Message, true, ErrorSessionImpact.None, ex);
            }
        }
    }
}
