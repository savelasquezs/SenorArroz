using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Authorize(Roles = "Superadmin, Admin, Cashier")]
[Route("api/whatsapp/ai-usage")]
public class WhatsAppAiUsageController(IApplicationDbContext db, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<WhatsAppAiUsageDto>>> Get(int? branchId, DateTime? from, DateTime? to, string? provider, string? model, CancellationToken ct)
    {
        if (!Roles.IsSuperadmin(currentUser.Role))
        {
            if (branchId.HasValue && branchId.Value != currentUser.BranchId) return Forbid();
            branchId = currentUser.BranchId;
        }
        if (branchId is <= 0) return BadRequest(ApiResponse<WhatsAppAiUsageDto>.ErrorResponse("La sucursal no es válida."));
        var end = NormalizeUtc(to ?? DateTime.UtcNow);
        var start = NormalizeUtc(from ?? end.AddDays(-30));
        if (start >= end) return BadRequest(ApiResponse<WhatsAppAiUsageDto>.ErrorResponse("El rango de fechas no es válido."));

        var q = db.WhatsAppAiInvocations.AsNoTracking().Where(x => x.CreatedAt >= start && x.CreatedAt < end);
        if (branchId.HasValue) q = q.Where(x => x.BranchId == branchId.Value);
        if (!string.IsNullOrWhiteSpace(provider)) q = q.Where(x => x.Provider == provider);
        if (!string.IsNullOrWhiteSpace(model)) q = q.Where(x => x.Model == model);

        var total = await q.CountAsync(ct);
        var messages = await q.Select(x => x.IncomingMessageId).Distinct().CountAsync(ct);
        var conversations = await q.Select(x => x.ConversationId).Distinct().CountAsync(ct);
        var aggregate = await q.GroupBy(_ => 1).Select(g => new { Input = g.Sum(x => (long)(x.InputTokens ?? 0)), Cached = g.Sum(x => (long)(x.CachedInputTokens ?? 0)), Output = g.Sum(x => (long)(x.OutputTokens ?? 0)), Cost = g.Sum(x => x.EstimatedCostUsd ?? 0), Unpriced = g.Count(x => x.EstimatedCostUsd == null), Errors = g.Count(x => !x.Success), Duration = g.Average(x => (double?)(x.DurationMs ?? 0)) ?? 0, Tools = g.Sum(x => x.ToolCallCount) }).FirstOrDefaultAsync(ct);
        var p95 = total == 0 ? 0 : await q.OrderBy(x => x.DurationMs).Select(x => x.DurationMs ?? 0).Skip(Math.Max(0, (int)Math.Ceiling(total * .95) - 1)).FirstAsync(ct);
        var breakdown = await q.GroupBy(x => new { x.Provider, x.Model }).Select(g => new WhatsAppAiUsageBreakdownDto(g.Key.Provider, g.Key.Model, g.Count(), g.Select(x => x.IncomingMessageId).Distinct().Count(), g.Sum(x => (long)(x.InputTokens ?? 0)), g.Sum(x => (long)(x.CachedInputTokens ?? 0)), g.Sum(x => (long)(x.OutputTokens ?? 0)), g.Sum(x => x.EstimatedCostUsd ?? 0), g.Count(x => x.EstimatedCostUsd == null), g.Average(x => (double)(x.DurationMs ?? 0)), (double)g.Count(x => !x.Success) / g.Count())).ToListAsync(ct);
        var daily = await q.GroupBy(x => x.CreatedAt.Date).OrderBy(g => g.Key).Select(g => new WhatsAppAiUsageDailyDto(g.Key, g.Count(), g.Sum(x => (long)(x.InputTokens ?? 0)), g.Sum(x => (long)(x.CachedInputTokens ?? 0)), g.Sum(x => (long)(x.OutputTokens ?? 0)), g.Sum(x => x.EstimatedCostUsd ?? 0), g.Count(x => x.EstimatedCostUsd == null))).ToListAsync(ct);
        var dto = new WhatsAppAiUsageDto { TotalInvocations = total, IncomingMessagesProcessed = messages, ConversationsServed = conversations, InputTokens = aggregate?.Input ?? 0, CachedInputTokens = aggregate?.Cached ?? 0, OutputTokens = aggregate?.Output ?? 0, EstimatedCostUsd = aggregate?.Cost ?? 0, UnpricedInvocations = aggregate?.Unpriced ?? 0, AverageDurationMs = aggregate?.Duration ?? 0, P95DurationMs = p95, ErrorRate = total == 0 ? 0 : (double)(aggregate?.Errors ?? 0) / total, AverageInvocationsPerMessage = messages == 0 ? 0 : (double)total / messages, AverageToolCallsPerMessage = messages == 0 ? 0 : (double)(aggregate?.Tools ?? 0) / messages, Breakdown = breakdown, Daily = daily };
        return Ok(ApiResponse<WhatsAppAiUsageDto>.SuccessResponse(dto, "Uso de IA obtenido."));
    }
    private static DateTime NormalizeUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
