using AnkiLearner.Api.Auth;
using AnkiLearner.Api.Contracts;
using AnkiLearner.Core.Entities;
using AnkiLearner.Infrastructure.Data;
using AnkiLearner.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AnkiLearner.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController(
    UserManager<AppUser> users,
    AppDbContext db,
    TokenService tokens,
    RefreshTokenService refreshTokens,
    IConfiguration config) : ControllerBase
{
    private const string RefreshCookie = "ankilearner_refresh";

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        if (!config.GetValue("Auth:AllowRegistration", true))
            return Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Registration is disabled on this instance.");

        var user = new AppUser { UserName = request.Email, Email = request.Email };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    ["register"] = result.Errors.Select(e => e.Description).ToArray(),
                }));

        db.UserSettings.Add(new UserSettings
        {
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        var response = await SignInAsync(user, ct);
        return CreatedAtAction(nameof(Me), response);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null || !await users.CheckPasswordAsync(user, request.Password))
            return Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid email or password.");

        return await SignInAsync(user, ct);
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken ct)
    {
        var raw = Request.Cookies[RefreshCookie];
        if (string.IsNullOrEmpty(raw))
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "No refresh token.");

        var rotated = await refreshTokens.RotateAsync(raw, ct);
        if (rotated is null)
        {
            DeleteRefreshCookie();
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Refresh token is no longer valid.");
        }

        var (userId, newRaw, expires) = rotated.Value;
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "User no longer exists.");

        SetRefreshCookie(newRaw, expires);
        return new AuthResponse(tokens.CreateAccessToken(user), new UserDto(user.Id, user.Email!));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var raw = Request.Cookies[RefreshCookie];
        if (!string.IsNullOrEmpty(raw))
            await refreshTokens.RevokeAsync(raw, ct);
        DeleteRefreshCookie();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    [DisableRateLimiting]
    public async Task<ActionResult<MeResponse>> Me(
        [FromServices] Core.Abstractions.ICurrentUser currentUser,
        [FromServices] Services.SettingsService settingsService,
        CancellationToken ct)
    {
        var user = await users.FindByIdAsync(currentUser.UserId.ToString());
        if (user is null) return NotFound();

        var settings = await settingsService.GetOrCreateAsync(user.Id, ct);
        return new MeResponse(
            new UserDto(user.Id, user.Email!),
            new SettingsDto(settings.LearningLanguage, settings.KnownLanguages, settings.DailyNewLimit));
    }

    private async Task<AuthResponse> SignInAsync(AppUser user, CancellationToken ct)
    {
        var (raw, expires) = await refreshTokens.IssueAsync(user.Id, ct);
        SetRefreshCookie(raw, expires);
        return new AuthResponse(tokens.CreateAccessToken(user), new UserDto(user.Id, user.Email!));
    }

    private void SetRefreshCookie(string raw, DateTime expires) =>
        Response.Cookies.Append(RefreshCookie, raw, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            Expires = expires,
        });

    private void DeleteRefreshCookie() =>
        Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/api/auth" });
}
