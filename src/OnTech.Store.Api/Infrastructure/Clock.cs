namespace OnTech.Store.Api.Infrastructure;

public static class DateExtensions
{
    public static DateTimeOffset ToUtcOffset(this DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
    public static DateTimeOffset? ToUtcOffset(this DateTime? utc) => utc?.ToUtcOffset();
}
