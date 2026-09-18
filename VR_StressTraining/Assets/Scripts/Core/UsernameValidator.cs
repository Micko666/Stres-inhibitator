using System;
using System.Collections.Generic;

namespace StressTraining.Core
{
    public enum UsernameValidationResult
    {
        Ok = 0,
        Empty = 1,
        TooShort = 2,
        TooLong = 3,
        DuplicateCaseInsensitive = 4,
        InvalidCharacters = 5
    }

    /// <summary>
    /// Username rules (spec §5):
    /// - trimmed; must not be empty
    /// - 2..24 characters after trimming
    /// - duplicates rejected case-insensitively
    /// - username is display-only; userId (GUID) is the primary key
    /// Pure static — fully testable without Unity.
    /// </summary>
    public static class UsernameValidator
    {
        public const int MinLength = 2;
        public const int MaxLength = 24;

        public static string Normalize(string raw) => (raw ?? string.Empty).Trim();

        public static UsernameValidationResult Validate(
            string candidate, IEnumerable<string> existingUsernames)
        {
            string name = Normalize(candidate);
            if (name.Length == 0) return UsernameValidationResult.Empty;
            if (name.Length < MinLength) return UsernameValidationResult.TooShort;
            if (name.Length > MaxLength) return UsernameValidationResult.TooLong;

            foreach (char c in name)
            {
                // letters (any script), digits, space, dash, underscore, dot
                if (!char.IsLetterOrDigit(c) && c != ' ' && c != '-' && c != '_' && c != '.')
                    return UsernameValidationResult.InvalidCharacters;
            }

            if (existingUsernames != null)
            {
                foreach (var existing in existingUsernames)
                {
                    if (string.Equals(Normalize(existing), name, StringComparison.OrdinalIgnoreCase))
                        return UsernameValidationResult.DuplicateCaseInsensitive;
                }
            }
            return UsernameValidationResult.Ok;
        }
    }
}
