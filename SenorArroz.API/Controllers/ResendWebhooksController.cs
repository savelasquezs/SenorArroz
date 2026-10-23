using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Helpers;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/webhooks/resend")]
public sealed class ResendWebhooksController(ApplicationDbContext db, ITenantExecutionContext tenants, IConfiguration configuration, IClock clock) : ControllerBase
{
    [HttpPost, AllowAnonymous, RequestSizeLimit(65536)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        var eventId = Request.Headers["svix-id"].ToString();
        if (!ResendWebhook.Verify(body, eventId, Request.Headers["svix-timestamp"].ToString(), Request.Headers["svix-signature"].ToString(), configuration["ResendSettings:WebhookSecret"], clock.UtcNow))
            return Unauthorized();
        ResendWebhook.DeliveryEvent? result;
        try { result = ResendWebhook.Read(body); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return BadRequest(); }
        if (result is null) return Ok();
        if (result.OccurredAt > clock.UtcNow.AddMinutes(5)) return BadRequest();
        int messageId, tenantId;
        using (tenants.BeginSystemScope())
        {
            var matches = await db.EmailOutboxMessages.AsNoTracking().Where(x => x.MessageType == "electronic_invoice"
                && (x.ProviderMessageId == result.EmailId || x.ProviderMessageId == null && x.Id == result.OutboxId))
                .Select(x => new { x.Id, x.TenantId }).Take(2).ToListAsync(ct);
            if (matches.Count != 1) return Ok();
            messageId = matches[0].Id; tenantId = matches[0].TenantId;
        }
        using (tenants.BeginTenantScope(tenantId))
            await new InvoiceDeliveryService(db, clock).RecordDeliveryAsync(messageId, result, eventId, ct);
        return Ok();
    }
}
