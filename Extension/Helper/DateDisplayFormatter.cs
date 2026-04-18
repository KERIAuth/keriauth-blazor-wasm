using System.Globalization;

namespace Extension.Helper;

public static class DateDisplayFormatter {
    public static string FormatHybrid(DateTimeOffset timestamp, DateTimeOffset now) {
        var localTimestamp = timestamp.ToLocalTime();
        var localNow = now.ToLocalTime();

        var elapsed = localNow - localTimestamp;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        if (elapsed < TimeSpan.FromMinutes(1))
            return "Just now";

        if (elapsed < TimeSpan.FromHours(1))
            return $"{(int)elapsed.TotalMinutes}m ago";

        if (elapsed < TimeSpan.FromHours(24))
            return $"{(int)elapsed.TotalHours}h ago";

        if (elapsed < TimeSpan.FromHours(48))
            return $"Yesterday {localTimestamp:HH:mm}";

        return localTimestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public static string FormatHybrid(DateTime timestamp, DateTimeOffset now) {
        var kind = timestamp.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : timestamp.Kind;
        var normalized = DateTime.SpecifyKind(timestamp, kind);
        return FormatHybrid(new DateTimeOffset(normalized), now);
    }
}
