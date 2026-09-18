using System;
using System.Globalization;

namespace StressTraining.Core
{
    /// <summary>
    /// Time conventions for the whole project:
    /// - Persistent records use UTC ISO-8601 round-trip strings ("o" format).
    /// - Durations use monotonic time (SessionClock / Stopwatch), never wall-clock deltas.
    /// </summary>
    public static class UtcTime
    {
        public static DateTime Now() => DateTime.UtcNow;

        public static string NowIso() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        public static string ToIso(DateTime utc) =>
            utc.Kind == DateTimeKind.Utc
                ? utc.ToString("o", CultureInfo.InvariantCulture)
                : utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

        public static bool TryParseIso(string iso, out DateTime utc)
        {
            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc))
                return true;
            utc = default;
            return false;
        }

        public static DateTime ParseIsoOrMin(string iso) =>
            TryParseIso(iso, out var dt) ? dt : DateTime.MinValue;
    }
}
