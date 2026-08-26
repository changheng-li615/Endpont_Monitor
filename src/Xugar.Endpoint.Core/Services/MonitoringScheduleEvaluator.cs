using System.Globalization;
using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Services;

public static class MonitoringScheduleEvaluator
{
    public static bool IsWithinSchedule(MonitoringPolicy policy, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!TryValidate(policy, out var timezone))
        {
            return false;
        }

        var local = TimeZoneInfo.ConvertTime(utcNow, timezone);
        var localTime = TimeOnly.FromDateTime(local.DateTime);
        var day = (int)local.DayOfWeek;
        var previousDay = (day + 6) % 7;

        foreach (var window in policy.ScheduleWindows)
        {
            var start = TimeOnly.ParseExact(window.StartLocalTime, "HH:mm", CultureInfo.InvariantCulture);
            var end = TimeOnly.ParseExact(window.EndLocalTime, "HH:mm", CultureInfo.InvariantCulture);
            if (start < end && window.DayOfWeek == day && localTime >= start && localTime < end)
            {
                return true;
            }

            if (start > end &&
                ((window.DayOfWeek == day && localTime >= start) ||
                 (window.DayOfWeek == previousDay && localTime < end)))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryValidate(MonitoringPolicy policy, out TimeZoneInfo timezone)
    {
        timezone = TimeZoneInfo.Utc;
        if (policy.Version < 0 ||
            policy.ScreenshotIntervalSeconds is < 60 or > 86_400 ||
            policy.ProcessIntervalSeconds is < 15 or > 86_400 ||
            string.IsNullOrWhiteSpace(policy.Timezone) ||
            policy.ScheduleWindows is null)
        {
            return false;
        }

        try
        {
            timezone = TimeZoneInfo.FindSystemTimeZoneById(policy.Timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }

        return policy.ScheduleWindows.All(window =>
            window.DayOfWeek is >= 0 and <= 6 &&
            TimeOnly.TryParseExact(
                window.StartLocalTime,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var start) &&
            TimeOnly.TryParseExact(
                window.EndLocalTime,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var end) &&
            start != end);
    }
}
