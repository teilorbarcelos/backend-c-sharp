using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace MageBackend.Shared
{
    [ExcludeFromCodeCoverage]
    public static class DateTimeHelper
    {
        public static DateTime ParseStartDate(string? dateStr, int defaultDaysAgo = 30)
        {
            if (string.IsNullOrEmpty(dateStr))
                return DateTime.UtcNow.AddDays(-defaultDaysAgo);

            return DateTime.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : DateTime.UtcNow.AddDays(-defaultDaysAgo);
        }

        public static DateTime ParseEndDate(string? dateStr)
        {
            if (string.IsNullOrEmpty(dateStr))
                return DateTime.UtcNow;

            return DateTime.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date.Date.AddDays(1).AddSeconds(-1)
                : DateTime.UtcNow;
        }
    }
}
