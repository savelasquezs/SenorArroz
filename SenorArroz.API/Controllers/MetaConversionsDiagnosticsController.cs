using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Authorize(Roles = "Superadmin, Admin")]
[Route("api/integrations/meta/conversions")]
public sealed class MetaConversionsDiagnosticsController(
    IApplicationDbContext db,
    IOptions<MetaConversionsOptions> metaOptions,
    IOptions<StorefrontCustomerAuthOptions> storefrontOptions) : ControllerBase
{
    private static readonly string[] PurchaseEventTypes = ["order_created_web_cash", "order_payment_approved"];

    [HttpGet("status")]
    public async Task<ActionResult<ApiResponse<object>>> Status(CancellationToken cancellationToken)
    {
        var tenantId = Math.Max(1, storefrontOptions.Value.TenantId);
        var since = DateTime.UtcNow.AddDays(-7);
        var query = db.PaymentNotificationOutboxMessages.AsNoTracking()
            .Where(x => x.TenantId == tenantId
                && PurchaseEventTypes.Contains(x.EventType)
                && x.CreatedAt >= since);

        var counts = await query
            .GroupBy(x => x.MetaStatus)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var latestProcessed = await query
            .Where(x => x.MetaStatus == "processed")
            .OrderByDescending(x => x.MetaProcessedAt)
            .Select(x => new { x.OrderId, x.MetaProcessedAt })
            .FirstOrDefaultAsync(cancellationToken);
        var latestFailure = await query
            .Where(x => x.MetaStatus == "failed" || (x.MetaStatus == "pending" && x.MetaLastError != null))
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => new { x.OrderId, x.MetaStatus, x.MetaAttemptCount, x.MetaLastError, x.MetaNextAttemptAt })
            .FirstOrDefaultAsync(cancellationToken);

        var options = metaOptions.Value;
        return Ok(ApiResponse<object>.SuccessResponse(new
        {
            configured = options.IsConfigured,
            pixelId = string.IsNullOrWhiteSpace(options.PixelId) ? null : options.PixelId,
            graphApiVersion = options.GraphApiVersion,
            eventSourceUrl = options.EventSourceUrl,
            testMode = !string.IsNullOrWhiteSpace(options.TestEventCode),
            windowDays = 7,
            processed = Count(counts, "processed"),
            pending = Count(counts, "pending"),
            failed = Count(counts, "failed"),
            ignored = Count(counts, "ignored"),
            latestProcessed,
            latestFailure,
        }));
    }

    private static int Count(IEnumerable<dynamic> counts, string status) =>
        counts.FirstOrDefault(x => string.Equals((string)x.Status, status, StringComparison.OrdinalIgnoreCase))?.Count ?? 0;
}
