namespace TransportPlanner.Application.Services;

public readonly record struct TimeWindow(
    bool IsClosed,
    int OpenMinute,
    int CloseMinute,
    int? OpenMinute2,
    int? CloseMinute2)
{
    public static TimeWindow AlwaysOpen => new(false, 0, 24 * 60, null, null);
    public bool HasSecondWindow => OpenMinute2.HasValue && CloseMinute2.HasValue;
}

public static class TimeWindowHelper
{
    public static TimeWindow BuildWindow(
        bool isClosed,
        TimeSpan? openTime,
        TimeSpan? closeTime,
        TimeSpan? openTime2 = null,
        TimeSpan? closeTime2 = null)
    {
        if (isClosed || !openTime.HasValue || !closeTime.HasValue)
        {
            return new TimeWindow(true, 0, 0, null, null);
        }

        var openMinute = (int)Math.Round(openTime.Value.TotalMinutes);
        var closeMinute = (int)Math.Round(closeTime.Value.TotalMinutes);
        if (openMinute < 0 || closeMinute <= openMinute)
        {
            return new TimeWindow(true, 0, 0, null, null);
        }

        int? openMinute2 = null;
        int? closeMinute2 = null;
        if (openTime2.HasValue && closeTime2.HasValue)
        {
            var secondOpen = (int)Math.Round(openTime2.Value.TotalMinutes);
            var secondClose = (int)Math.Round(closeTime2.Value.TotalMinutes);
            if (secondOpen >= 0 && secondClose > secondOpen && secondOpen >= closeMinute)
            {
                openMinute2 = secondOpen;
                closeMinute2 = secondClose;
            }
        }

        return new TimeWindow(false, openMinute, closeMinute, openMinute2, closeMinute2);
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

        if (TryScheduleInRange(window.OpenMinute, window.CloseMinute, arrivalMinute, serviceMinutes, out waitMinutes, out startServiceMinute, out endServiceMinute))
        {
            return true;
        }

        if (window.HasSecondWindow)
        {
            return TryScheduleInRange(window.OpenMinute2!.Value, window.CloseMinute2!.Value, arrivalMinute, serviceMinutes, out waitMinutes, out startServiceMinute, out endServiceMinute);
        }

        return false;
    }

    private static bool TryScheduleInRange(
        int openMinute,
        int closeMinute,
        int arrivalMinute,
        int serviceMinutes,
        out int waitMinutes,
        out int startServiceMinute,
        out int endServiceMinute)
    {
        waitMinutes = arrivalMinute < openMinute ? openMinute - arrivalMinute : 0;
        startServiceMinute = arrivalMinute + waitMinutes;
        endServiceMinute = startServiceMinute + serviceMinutes;
        return endServiceMinute <= closeMinute;
    }
}
