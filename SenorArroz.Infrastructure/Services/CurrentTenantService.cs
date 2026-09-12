using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Services;

public sealed class CurrentTenantService : ICurrentTenant
{
    private readonly IHttpContextAccessor _http;
    private readonly int _configuredTenantId;

    public CurrentTenantService(IHttpContextAccessor http, IConfiguration configuration)
    {
        _http = http;
        _configuredTenantId = int.TryParse(configuration["Tenant:DefaultTenantId"]
            ?? configuration["StorefrontCustomerAuth:TenantId"], out var value) && value > 0 ? value : 1;
    }

    public int TenantId
    {
        get
        {
            var claim = _http.HttpContext?.User.FindFirst("tenant_id")?.Value;
            return int.TryParse(claim, out var value) && value > 0 ? value : _configuredTenantId;
        }
    }

    public bool HasTenant => TenantId > 0;
}
