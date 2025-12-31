using TransportPlanner.Application.Services;
using Xunit;

namespace TransportPlanner.Tests;

public class TimeWindowHelperTests
{
    [Fact]
    public void TrySchedule_ReturnsWaitAndServiceWindow()
    {
        var window = TimeWindowHelper.BuildWindow(false, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));

        var ok = TimeWindowHelper.TrySchedule(
            window,
            arrivalMinute: 8 * 60 + 30,
            serviceMinutes: 30,
            out var waitMinutes,
            out var startServiceMinute,
            out var endServiceMinute);

        Assert.True(ok);
        Assert.Equal(30, waitMinutes);
        Assert.Equal(9 * 60, startServiceMinute);
        Assert.Equal(9 * 60 + 30, endServiceMinute);
    }

    [Fact]
    public void TrySchedule_ReturnsFalseWhenOutsideWindow()
    {
        var window = TimeWindowHelper.BuildWindow(false, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));

        var ok = TimeWindowHelper.TrySchedule(
            window,
            arrivalMinute: 16 * 60 + 50,
            serviceMinutes: 30,
            out _,
            out _,
            out _);

        Assert.False(ok);
    }
}
