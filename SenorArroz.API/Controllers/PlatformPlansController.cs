using Microsoft.AspNetCore.Mvc;
using SenorArroz.API.Filters;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Saas.DTOs;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/platform/plans")]
[PlatformAuthorize]
public sealed class PlatformPlansController : ControllerBase
{
    private readonly IPlatformService _platform;
    public PlatformPlansController(IPlatformService platform) => _platform = platform;
    [HttpGet] public async Task<IActionResult> List(CancellationToken cancellationToken) => Ok(await _platform.GetPlansAsync(cancellationToken));
    [HttpPost] public async Task<IActionResult> Create(CreatePlatformPlanRequest request, CancellationToken cancellationToken) => Ok(await _platform.CreatePlanAsync(request, Context(), cancellationToken));
    [HttpPost("{planId:int}/versions")] public async Task<IActionResult> CreateVersion(int planId, UpsertPlanVersionRequest request, CancellationToken cancellationToken) => Ok(await _platform.CreatePlanVersionAsync(planId, request, Context(), cancellationToken));
    [HttpPut("versions/{versionId:int}")] public async Task<IActionResult> UpdateVersion(int versionId, UpsertPlanVersionRequest request, CancellationToken cancellationToken) => Ok(await _platform.UpdatePlanVersionAsync(versionId, request, Context(), cancellationToken));
    [HttpPost("versions/{versionId:int}/publish")] public async Task<IActionResult> Publish(int versionId, CancellationToken cancellationToken) => Ok(await _platform.PublishPlanVersionAsync(versionId, Context(), cancellationToken));
    [HttpPost("versions/{versionId:int}/retire")] public async Task<IActionResult> Retire(int versionId, CancellationToken cancellationToken) => Ok(await _platform.RetirePlanVersionAsync(versionId, Context(), cancellationToken));
    private PlatformRequestContext Context() => new(HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", Request.Headers.UserAgent.ToString(), HttpContext.TraceIdentifier);
}

[ApiController]
[Route("api/platform")]
[PlatformAuthorize]
public sealed class PlatformConfigurationController : ControllerBase
{
    private readonly IPlatformService _platform;
    public PlatformConfigurationController(IPlatformService platform) => _platform = platform;
    [HttpGet("modules")] public async Task<IActionResult> Modules(CancellationToken cancellationToken) => Ok(await _platform.GetModulesAsync(cancellationToken));
    [HttpGet("addons")] public async Task<IActionResult> Addons(CancellationToken cancellationToken) => Ok(await _platform.GetAddonsAsync(cancellationToken));
    [HttpPost("modules")] public async Task<IActionResult> CreateModule(UpsertPlatformCatalogItemRequest request, CancellationToken cancellationToken) => Ok(await _platform.UpsertModuleAsync(null, request, Context(), cancellationToken));
    [HttpPut("modules/{id:int}")] public async Task<IActionResult> UpdateModule(int id, UpsertPlatformCatalogItemRequest request, CancellationToken cancellationToken) => Ok(await _platform.UpsertModuleAsync(id, request, Context(), cancellationToken));
    [HttpPost("addons")] public async Task<IActionResult> CreateAddon(UpsertPlatformCatalogItemRequest request, CancellationToken cancellationToken) => Ok(await _platform.UpsertAddonAsync(null, request, Context(), cancellationToken));
    [HttpPut("addons/{id:int}")] public async Task<IActionResult> UpdateAddon(int id, UpsertPlatformCatalogItemRequest request, CancellationToken cancellationToken) => Ok(await _platform.UpsertAddonAsync(id, request, Context(), cancellationToken));
    [HttpGet("settings")] public async Task<IActionResult> Settings(CancellationToken cancellationToken) => Ok(await _platform.GetSettingsAsync(cancellationToken));
    [HttpPut("settings")] public async Task<IActionResult> UpdateSettings(Dictionary<string, string> request, CancellationToken cancellationToken) => Ok(await _platform.UpdateSettingsAsync(request, Context(), cancellationToken));
    [HttpGet("audit")] public async Task<IActionResult> Audit([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) => Ok(await _platform.GetAuditAsync(page, pageSize, cancellationToken));
    private PlatformRequestContext Context() => new(HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", Request.Headers.UserAgent.ToString(), HttpContext.TraceIdentifier);
}

[ApiController]
[Route("api/tenant/context")]
[Microsoft.AspNetCore.Authorization.Authorize]
public sealed class TenantContextController : ControllerBase
{
    private readonly ITenantCapabilityService _capabilities;
    public TenantContextController(ITenantCapabilityService capabilities) => _capabilities = capabilities;
    [HttpGet] public async Task<IActionResult> Get(CancellationToken cancellationToken) => Ok(await _capabilities.GetCurrentAsync(cancellationToken));
}
