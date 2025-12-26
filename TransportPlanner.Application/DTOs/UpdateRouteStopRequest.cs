namespace TransportPlanner.Application.DTOs;

public class UpdateRouteStopRequest
{
    /// <summary>
    /// If set, records when the driver arrived at the stop (UTC).
    /// </summary>
    public DateTime? ArrivedAtUtc { get; set; }

    /// <summary>
    /// If set, records when the driver completed the stop (UTC).
    /// </summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>
    /// If set, records the actual visit duration in minutes.
    /// </summary>
    public int? ActualServiceMinutes { get; set; }

    /// <summary>
    /// Optional note/remark from the driver.
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// Optional status override (e.g., NotVisited).
    /// </summary>
    public string? Status { get; set; }
}


