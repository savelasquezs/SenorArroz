using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SenorArroz.API.Filters;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Saas.DTOs;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/platform/auth")]
public sealed class PlatformAuthController : ControllerBase
{
    private readonly IPlatformAuthService _auth;
    private readonly IPlatformCurrentUser _currentUser;
    private readonly IWebHostEnvironment _environment;

    public PlatformAuthController(IPlatformAuthService auth, IPlatformCurrentUser currentUser, IWebHostEnvironment environment)
    {
        _auth = auth;
        _currentUser = currentUser;
        _environment = environment;
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth-sensitive")]
    public async Task<IActionResult> Login(PlatformLoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _auth.LoginAsync(request, Request.Cookies[PlatformSessionFilter.TrustedDeviceCookie], BuildContext(), cancellationToken);
        if (result.SessionToken is not null)
        {
            SetSessionCookie(result.SessionToken);
            SetCsrfCookie(result.CsrfToken!);
        }
        return Ok(new { result.OtpRequired, result.ChallengeId, result.ChallengeExpiresAt, result.User, result.CsrfToken });
    }

    [HttpPost("otp")]
    [EnableRateLimiting("auth-sensitive")]
    public async Task<IActionResult> VerifyOtp(PlatformVerifyOtpRequest request, CancellationToken cancellationToken)
    {
        var result = await _auth.VerifyOtpAsync(request, BuildContext(), cancellationToken);
        SetSessionCookie(result.SessionToken!);
        SetCsrfCookie(result.CsrfToken!);
        Response.Cookies.Append(PlatformSessionFilter.TrustedDeviceCookie, result.TrustedDeviceToken!, CookieOptions(TimeSpan.FromDays(30), "/api/platform/auth"));
        return Ok(new { result.User, result.CsrfToken });
    }

    [HttpGet("session")]
    [PlatformAuthorize]
    public IActionResult Session() => Ok(new PlatformSessionDto(_currentUser.Id, _currentUser.Name, _currentUser.Email));

    [HttpPost("logout")]
    [PlatformAuthorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await _auth.LogoutAsync(Request.Cookies[PlatformSessionFilter.SessionCookie], BuildContext(), cancellationToken);
        Response.Cookies.Delete(PlatformSessionFilter.SessionCookie, new CookieOptions { Path = "/api/platform" });
        Response.Cookies.Delete(PlatformSessionFilter.CsrfCookie, new CookieOptions { Path = "/" });
        return NoContent();
    }

    [HttpGet("devices")]
    [PlatformAuthorize]
    public async Task<IActionResult> Devices(CancellationToken cancellationToken) => Ok(await _auth.GetTrustedDevicesAsync(cancellationToken));

    [HttpDelete("devices/{publicId:guid}")]
    [PlatformAuthorize]
    public async Task<IActionResult> RevokeDevice(Guid publicId, CancellationToken cancellationToken)
    {
        await _auth.RevokeTrustedDeviceAsync(publicId, BuildContext(), cancellationToken);
        return NoContent();
    }

    private void SetSessionCookie(string token) => Response.Cookies.Append(PlatformSessionFilter.SessionCookie, token, CookieOptions(TimeSpan.FromHours(12), "/api/platform"));
    private void SetCsrfCookie(string token) => Response.Cookies.Append(PlatformSessionFilter.CsrfCookie, token, CookieOptions(TimeSpan.FromHours(12), "/", false));
    private CookieOptions CookieOptions(TimeSpan duration, string path, bool httpOnly = true) => new() { HttpOnly = httpOnly, Secure = !_environment.IsDevelopment(), SameSite = SameSiteMode.Strict, Path = path, Expires = DateTimeOffset.UtcNow.Add(duration) };
    private PlatformRequestContext BuildContext() => new(GetIp(), Request.Headers.UserAgent.ToString(), HttpContext.TraceIdentifier);
    private string GetIp() => Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim() ?? HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
