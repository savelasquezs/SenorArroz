using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Services;

public sealed class CurrentTenantService : ICurrentTenant
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly int _configuredTenantId;
    private readonly Guid? _configuredTenantPublicId;
    private readonly long? _configuredAccessVersion;

    public CurrentTenantService(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    {
        _httpContextAccessor = httpContextAccessor;
        _configuredTenantId = configuration.GetValue("Multitenancy:DefaultTenantId", 1);
        _configuredTenantPublicId = Guid.TryParse(
            configuration["Multitenancy:DefaultTenantPublicId"],
            out var publicId)
            ? publicId
            : null;
        _configuredAccessVersion = long.TryParse(
            configuration["Multitenancy:DefaultTenantAccessVersion"],
            out var accessVersion)
            ? accessVersion
            : null;
    }

    public int TenantId => ResolveTenantId();
    public Guid? TenantPublicId => ResolveGuidClaim("tenant_public_id") ?? ConfiguredValue(_configuredTenantPublicId);
    public long? AccessVersion => ResolveLongClaim("tenant_access_version") ?? ConfiguredValue(_configuredAccessVersion);
    public bool HasTenant => TenantId > 0;

    private int ResolveTenantId()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context?.User.Identity?.IsAuthenticated == true)
        {
            var value = context.User.FindFirst("tenant_id")?.Value;
            return int.TryParse(value, out var tenantId) && tenantId > 0 ? tenantId : 0;
        }

        return _configuredTenantId > 0 ? _configuredTenantId : 0;
    }

    private Guid? ResolveGuidClaim(string name)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context?.User.Identity?.IsAuthenticated != true)
            return null;

        return Guid.TryParse(context.User.FindFirst(name)?.Value, out var value) ? value : null;
    }

    private long? ResolveLongClaim(string name)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context?.User.Identity?.IsAuthenticated != true)
            return null;

        return long.TryParse(context.User.FindFirst(name)?.Value, out var value) ? value : null;
    }

    private T? ConfiguredValue<T>(T? value) where T : struct =>
        _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true ? null : value;
}
