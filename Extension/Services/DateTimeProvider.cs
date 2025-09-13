namespace Extension.Services;

public class DateTimeProvider : IDateTimeProvider {
    private DateTime? DateTimeOverride { get; set; }

    public DateTimeProvider(DateTime? dateTimeOverride = null) {
        if (dateTimeOverride is not null) {
            DateTimeOverride = dateTimeOverride;
        }
    }

    public DateTime GetCurrentDatTimeUtc() {
        if (DateTimeOverride is not null) {
            return DateTimeOverride.Value;
        }
        return DateTime.UtcNow;
    }
}
