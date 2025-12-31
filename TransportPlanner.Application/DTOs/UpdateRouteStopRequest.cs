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
    /// Optional driver note (separate from planner notes).
    /// </summary>
    public string? DriverNote { get; set; }

    /// <summary>
    /// Optional issue code (e.g., access denied, customer not present).
    /// </summary>
    public string? IssueCode { get; set; }

    /// <summary>
    /// Marks whether follow-up is required.
    /// </summary>
    public bool? FollowUpRequired { get; set; }

    /// <summary>
    /// Optional proof status override.
    /// </summary>
    public string? ProofStatus { get; set; }

    /// <summary>
    /// Optional status override (e.g., NotVisited).
    /// </summary>
    public string? Status { get; set; }
}


