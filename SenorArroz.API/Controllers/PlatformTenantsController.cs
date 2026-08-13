using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SenorArroz.API.Filters;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Saas.DTOs;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/platform/tenants")]
[PlatformAuthorize]
public sealed class PlatformTenantsController : ControllerBase
{
    private readonly IPlatformService _platform;
    public PlatformTenantsController(IPlatformService platform) => _platform = platform;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] string? status, CancellationToken cancellationToken) => Ok(await _platform.GetTenantsAsync(search, status, cancellationToken));
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) => Ok(await _platform.GetTenantAsync(id, cancellationToken));
    [HttpPost]
    public async Task<IActionResult> Create(CreatePlatformTenantRequest request, CancellationToken cancellationToken) => Ok(await _platform.CreateTenantAsync(request, Context(), cancellationToken));
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdatePlatformTenantRequest request, CancellationToken cancellationToken) => Ok(await _platform.UpdateTenantAsync(id, request, Context(), cancellationToken));
    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> Status(int id, ChangeTenantStatusRequest request, CancellationToken cancellationToken) => Ok(await _platform.ChangeTenantStatusAsync(id, request, Context(), cancellationToken));
    [HttpPut("{id:int}/subscription")]
    public async Task<IActionResult> Subscription(int id, ChangeTenantSubscriptionRequest request, CancellationToken cancellationToken) => Ok(await _platform.ChangeSubscriptionAsync(id, request, Context(), cancellationToken));
    [HttpPut("{id:int}/addons/{code}")]
    public async Task<IActionResult> Addon(int id, string code, [FromBody] SetAddonRequest request, CancellationToken cancellationToken) => Ok(await _platform.SetAddonAsync(id, code, request.Active, Context(), cancellationToken));
    [HttpPost("{id:int}/invitation")]
    public async Task<IActionResult> Invitation(int id, CancellationToken cancellationToken) { await _platform.ResendInvitationAsync(id, Context(), cancellationToken); return NoContent(); }

    private PlatformRequestContext Context() => new(Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim() ?? HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", Request.Headers.UserAgent.ToString(), HttpContext.TraceIdentifier);
    public sealed record SetAddonRequest(bool Active);
}

[ApiController]
[Route("api/tenant-invitations")]
public sealed class TenantInvitationsController : ControllerBase
{
    private readonly IPlatformService _platform;
    public TenantInvitationsController(IPlatformService platform) => _platform = platform;
    [HttpPost("accept")]
    [EnableRateLimiting("auth-sensitive")]
    public async Task<IActionResult> Accept(AcceptTenantInvitationRequest request, CancellationToken cancellationToken) { await _platform.AcceptInvitationAsync(request, cancellationToken); return NoContent(); }
}
