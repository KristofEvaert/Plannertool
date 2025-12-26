namespace TransportPlanner.Infrastructure.Options;

public class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "TransportPlanner";
    public string Audience { get; set; } = "TransportPlannerClient";
    public string Key { get; set; } = string.Empty;
    public int AccessTokenLifetimeMinutes { get; set; } = 60;
}

