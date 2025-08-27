using System;

/// <summary>
/// Small date/time utility helpers.
/// </summary>
static class DateUtils
{
    /// <summary>
    /// Returns the whole number of days that have passed since the provided date.
    /// Uses UTC to avoid local-time ambiguities. If the date is in the future the result will be negative.
    /// </summary>
    public static int DaysSince(DateTime date)
    {
        var utcNow = DateTime.UtcNow;
        var utcDate = date.Kind == DateTimeKind.Utc ? date : date.ToUniversalTime();
        var diff = utcNow - utcDate;
        return (int)Math.Floor(diff.TotalDays);
    }
}
