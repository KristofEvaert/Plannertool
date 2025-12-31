namespace TransportPlanner.Application.Services;

public readonly record struct TimeWindow(bool IsClosed, int OpenMinute, int CloseMinute)
{
    public static TimeWindow AlwaysOpen => new(false, 0, 24 * 60);
}

public static class TimeWindowHelper
{
    public static TimeWindow BuildWindow(bool isClosed, TimeSpan? openTime, TimeSpan? closeTime)
    {
        if (isClosed || !openTime.HasValue || !closeTime.HasValue)
        {
            return new TimeWindow(true, 0, 0);
        }

        var openMinute = (int)Math.Round(openTime.Value.TotalMinutes);
        var closeMinute = (int)Math.Round(closeTime.Value.TotalMinutes);
        if (openMinute < 0 || closeMinute <= openMinute)
        {
            return new TimeWindow(true, 0, 0);
        }

        return new TimeWindow(false, openMinute, closeMinute);
    }

    public static bool TrySchedule(
        TimeWindow window,
        int arrivalMinute,
        int serviceMinutes,
        out int waitMinutes,
        out int startServiceMinute,
        out int endServiceMinute)
    {
        waitMinutes = 0;
        startServiceMinute = arrivalMinute;
        endServiceMinute = arrivalMinute;

        if (window.IsClosed)
        {
            return false;
        }

        waitMinutes = arrivalMinute < window.OpenMinute ? window.OpenMinute - arrivalMinute : 0;
        startServiceMinute = arrivalMinute + waitMinutes;
        endServiceMinute = startServiceMinute + serviceMinutes;
        return endServiceMinute <= window.CloseMinute;
    }
}
