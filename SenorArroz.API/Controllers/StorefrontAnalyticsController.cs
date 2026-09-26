using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.API.Security;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = StorefrontApiKeyOptions.Scheme)]
[Route("api/public/storefront/analytics")]
public sealed class PublicStorefrontAnalyticsController(
    IApplicationDbContext db,
    ITenantExecutionContext tenantExecutionContext,
    IOptions<StorefrontCustomerAuthOptions> storefrontOptions) : ControllerBase
{
    private readonly int _tenantId = storefrontOptions.Value.TenantId > 0
        ? storefrontOptions.Value.TenantId
        : throw new InvalidOperationException("StorefrontCustomerAuth:TenantId debe ser mayor que cero.");

    [HttpPost("events")]
    [RequestSizeLimit(8 * 1024)]
    public async Task<IActionResult> Record(
        [FromBody] StorefrontOperationalEventRequest request,
        CancellationToken cancellationToken)
    {
        var eventName = Clean(request.Event, 64);
        if (string.IsNullOrWhiteSpace(eventName)
            || eventName.Any(character => !(char.IsLetterOrDigit(character) || character == '_')))
            return BadRequest();

        var path = Clean(request.Path, 160);
        if (!string.IsNullOrEmpty(path) && !path.StartsWith('/')) path = string.Empty;

        var source = Clean(request.Campaign?.Source, 80);
        var medium = Clean(request.Campaign?.Medium, 80);
        var campaign = Clean(request.Campaign?.Campaign, 120);
        var content = Clean(request.Campaign?.Content, 120);
        var term = Clean(request.Campaign?.Term, 120);

        var branchId = ReadInt(request.Dimensions, "branch_id", 0, 1_000_000);
        var fulfillment = Clean(ReadString(request.Dimensions, "fulfillment_type"), 24);
        var payment = Clean(ReadString(request.Dimensions, "payment_type"), 24);
        var statusCode = ReadInt(request.Dimensions, "status_code", 0, 999);
        var withinCoverage = ReadBool(request.Dimensions, "within_coverage");
        var outsideCoverage = ReadBool(request.Dimensions, "outside_coverage");
        var coverageState = withinCoverage == true ? (short)1 : outsideCoverage == true ? (short)-1 : (short)0;
        var value = Math.Clamp(ReadDecimal(request.Dimensions, "value"), 0m, 100_000_000m);

        using var tenantScope = tenantExecutionContext.BeginTenantScope(_tenantId);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO storefront_analytics_daily (
                tenant_id, event_date, event_name, path,
                utm_source, utm_medium, utm_campaign, utm_content, utm_term,
                branch_id, fulfillment_type, payment_type, status_code, coverage_state,
                event_count, value_sum, created_at, updated_at
            )
            VALUES (
                {_tenantId}, (CURRENT_TIMESTAMP AT TIME ZONE 'America/Bogota')::date, {eventName}, {path},
                {source}, {medium}, {campaign}, {content}, {term},
                {branchId}, {fulfillment}, {payment}, {statusCode}, {coverageState},
                1, {value}, NOW(), NOW()
            )
            ON CONFLICT (
                tenant_id, event_date, event_name, path,
                utm_source, utm_medium, utm_campaign, utm_content, utm_term,
                branch_id, fulfillment_type, payment_type, status_code, coverage_state
            )
            DO UPDATE SET
                event_count = storefront_analytics_daily.event_count + 1,
                value_sum = storefront_analytics_daily.value_sum + EXCLUDED.value_sum,
                updated_at = NOW();
            """, cancellationToken);

        return NoContent();
    }

    private static string Clean(string? value, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? ReadString(IReadOnlyDictionary<string, JsonElement>? values, string key)
        => values is not null
            && values.TryGetValue(key, out var element)
            && element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : null;

    private static int ReadInt(IReadOnlyDictionary<string, JsonElement>? values, string key, int min, int max)
    {
        if (values is null || !values.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.Number)
            return 0;
        return element.TryGetInt32(out var value) ? Math.Clamp(value, min, max) : 0;
    }

    private static decimal ReadDecimal(IReadOnlyDictionary<string, JsonElement>? values, string key)
    {
        if (values is null || !values.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.Number)
            return 0;
        return element.TryGetDecimal(out var value) ? value : 0;
    }

    private static bool? ReadBool(IReadOnlyDictionary<string, JsonElement>? values, string key)
    {
        if (values is null || !values.TryGetValue(key, out var element))
            return null;
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }
}

[ApiController]
[Authorize(Roles = "Superadmin, Admin")]
[Route("api/integrations/meta/analytics")]
public sealed class StorefrontAnalyticsDiagnosticsController(
    IApplicationDbContext db,
    ICurrentTenant currentTenant,
    IBranchContext branchContext) : ControllerBase
{
    [HttpGet("funnel")]
    public async Task<ActionResult<ApiResponse<object>>> Funnel(
        [FromQuery] int days = 7,
        CancellationToken cancellationToken = default)
    {
        days = Math.Clamp(days, 1, 31);
        var localToday = DateTime.UtcNow.AddHours(-5).Date;
        var since = DateTime.SpecifyKind(localToday.AddDays(-(days - 1)), DateTimeKind.Unspecified);
        var tenantId = currentTenant.TenantId;
        var branchId = branchContext.ResolveOptional();

        var rows = await db.StorefrontAnalyticsDaily
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId
                && x.EventDate >= since
                && (!branchId.HasValue || x.BranchId == 0 || x.BranchId == branchId.Value))
            .GroupBy(x => new
            {
                x.EventDate,
                x.EventName,
                x.UtmSource,
                x.UtmMedium,
                x.UtmCampaign,
                x.UtmContent,
                x.BranchId,
                x.FulfillmentType,
                x.PaymentType,
            })
            .Select(group => new
            {
                date = group.Key.EventDate,
                eventName = group.Key.EventName,
                source = group.Key.UtmSource,
                medium = group.Key.UtmMedium,
                campaign = group.Key.UtmCampaign,
                content = group.Key.UtmContent,
                branchId = group.Key.BranchId == 0 ? null : (int?)group.Key.BranchId,
                fulfillmentType = group.Key.FulfillmentType,
                paymentType = group.Key.PaymentType,
                count = group.Sum(x => x.EventCount),
                value = group.Sum(x => x.ValueSum),
            })
            .OrderBy(x => x.date)
            .ThenBy(x => x.eventName)
            .ThenBy(x => x.campaign)
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<object>.SuccessResponse(new
        {
            windowDays = days,
            generatedAt = DateTime.UtcNow,
            rows,
        }));
    }
}

public sealed class StorefrontOperationalEventRequest
{
    public string? Event { get; set; }
    public string? Path { get; set; }
    public StorefrontOperationalCampaign? Campaign { get; set; }
    public Dictionary<string, JsonElement>? Dimensions { get; set; }
}

public sealed class StorefrontOperationalCampaign
{
    public string? Source { get; set; }
    public string? Medium { get; set; }
    public string? Campaign { get; set; }
    public string? Content { get; set; }
    public string? Term { get; set; }
}
