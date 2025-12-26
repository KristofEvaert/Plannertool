using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TransportPlanner.Application.DTOs;
using TransportPlanner.Infrastructure.Identity;
using TransportPlanner.Infrastructure.Options;
using TransportPlanner.Infrastructure.Services;

namespace TransportPlanner.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly JwtOptions _jwtOptions;

    public AuthController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IJwtTokenService jwtTokenService,
        Microsoft.Extensions.Options.IOptions<JwtOptions> jwtOptions)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _jwtTokenService = jwtTokenService;
        _jwtOptions = jwtOptions.Value;
    }

    private static string GetDisplayNameFromEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return string.Empty;
        var atIndex = email.IndexOf('@');
        return atIndex > 0 ? email[..atIndex] : email;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            // Auto-provision a basic account with no roles for first-time logins
            user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                Email = request.Email,
                UserName = request.Email,
                DisplayName = GetDisplayNameFromEmail(request.Email),
                EmailConfirmed = true
            };

            var createResult = await _userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
            {
                return BadRequest(new { message = string.Join("; ", createResult.Errors.Select(e => e.Description)) });
            }
        }
        else
        {
            var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: false);
            if (!result.Succeeded)
            {
                return Unauthorized(new { message = "Invalid credentials" });
            }
        }

        var roles = await _userManager.GetRolesAsync(user);
        var token = await _jwtTokenService.GenerateAsync(user, roles, cancellationToken);

        var response = new LoginResponse
        {
            Token = token,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenLifetimeMinutes),
            Email = user.Email ?? string.Empty,
            DisplayName = user.DisplayName,
            UserId = user.Id,
            Roles = roles.ToList()
        };

        return Ok(response);
    }
}
