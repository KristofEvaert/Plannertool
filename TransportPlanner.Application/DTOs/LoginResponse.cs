namespace TransportPlanner.Application.DTOs;

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public Guid UserId { get; set; }
    public List<string> Roles { get; set; } = new();
}

