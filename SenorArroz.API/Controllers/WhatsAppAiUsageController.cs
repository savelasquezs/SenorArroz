using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController, Authorize(Roles = "Superadmin, Admin, Cashier"), Route("api/whatsapp/ai-usage")]
public class WhatsAppAiUsageController(IApplicationDbContext db, ICurrentUser currentUser) : ControllerBase
{
    private static readonly TimeZoneInfo Colombia = ResolveColombia();

    [HttpGet]
    public async Task<ActionResult<ApiResponse<WhatsAppAiUsageDto>>> Get(int? branchId, DateOnly? fromDate, DateOnly? toDate, string? provider, string? model, CancellationToken ct)
    {
        if (!Roles.IsSuperadmin(currentUser.Role)) { if (branchId.HasValue && branchId != currentUser.BranchId) return Forbid(); branchId = currentUser.BranchId; }
        if (branchId is <= 0) return BadRequest(ApiResponse<WhatsAppAiUsageDto>.ErrorResponse("La sucursal no es válida."));
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Colombia));
        var from = fromDate ?? today.AddDays(-29); var to = toDate ?? today;
        if (from > to || to.DayNumber - from.DayNumber > 365) return BadRequest(ApiResponse<WhatsAppAiUsageDto>.ErrorResponse("El rango debe ser válido y no superar 366 días."));
        var startUtc = ToUtc(from); var endUtc = ToUtc(to.AddDays(1));
        var q = db.WhatsAppAiInvocations.AsNoTracking().Where(x => x.CreatedAt >= startUtc && x.CreatedAt < endUtc);
        if (branchId.HasValue) q = q.Where(x => x.BranchId == branchId);
        var normalizedProvider = provider?.Trim().ToLower(); var normalizedModel = model?.Trim().ToLower();
        if (!string.IsNullOrEmpty(normalizedProvider)) q = q.Where(x => x.Provider.Trim().ToLower() == normalizedProvider);
        if (!string.IsNullOrEmpty(normalizedModel)) q = q.Where(x => x.Model.Trim().ToLower() == normalizedModel);

        var total = await q.CountAsync(ct); var messages = await q.Select(x => x.IncomingMessageId).Distinct().CountAsync(ct); var conversations = await q.Select(x => x.ConversationId).Distinct().CountAsync(ct);
        var a = await q.GroupBy(_ => 1).Select(g => new { Input=g.Sum(x=>(long)(x.InputTokens??0)), Cached=g.Sum(x=>(long)(x.CachedInputTokens??0)), Output=g.Sum(x=>(long)(x.OutputTokens??0)), Thinking=g.Sum(x=>(long)(x.ThinkingTokens??0)), Billable=g.Sum(x=>(long)(x.BillableOutputTokens??x.OutputTokens??0)), Cost=g.Sum(x=>x.EstimatedCostUsd??0), Unpriced=g.Count(x=>x.EstimatedCostUsd==null), Errors=g.Count(x=>!x.Success), Duration=g.Average(x=>(double?)x.DurationMs)??0, Tools=g.Sum(x=>x.ToolCallCount)}).FirstOrDefaultAsync(ct);
        var durationQuery = q.Where(x=>x.DurationMs.HasValue);
        var durationCount = await durationQuery.CountAsync(ct);
        var p95 = durationCount == 0 ? 0 : await durationQuery.OrderBy(x=>x.DurationMs).Select(x=>x.DurationMs!.Value).Skip(P95Index(durationCount)).FirstOrDefaultAsync(ct);
        var breakdown = await q.GroupBy(x=>new{x.Provider,x.Model}).Select(g=>new WhatsAppAiUsageBreakdownDto(g.Key.Provider,g.Key.Model,g.Count(),g.Select(x=>x.IncomingMessageId).Distinct().Count(),g.Sum(x=>(long)(x.InputTokens??0)),g.Sum(x=>(long)(x.CachedInputTokens??0)),g.Sum(x=>(long)(x.OutputTokens??0)),g.Sum(x=>(long)(x.ThinkingTokens??0)),g.Sum(x=>(long)(x.BillableOutputTokens??x.OutputTokens??0)),g.Sum(x=>x.EstimatedCostUsd??0),g.Count(x=>x.EstimatedCostUsd==null),g.Average(x=>(double?)x.DurationMs)??0,(double)g.Count(x=>!x.Success)/g.Count())).ToListAsync(ct);
        var daily = await q.GroupBy(x=>x.CreatedAt.AddHours(-5).Date).OrderBy(g=>g.Key).Select(g=>new WhatsAppAiUsageDailyDto(g.Key,g.Count(),g.Sum(x=>(long)(x.InputTokens??0)),g.Sum(x=>(long)(x.CachedInputTokens??0)),g.Sum(x=>(long)(x.OutputTokens??0)),g.Sum(x=>(long)(x.ThinkingTokens??0)),g.Sum(x=>(long)(x.BillableOutputTokens??x.OutputTokens??0)),g.Sum(x=>x.EstimatedCostUsd??0),g.Count(x=>x.EstimatedCostUsd==null))).ToListAsync(ct);
        var dto = new WhatsAppAiUsageDto { TotalInvocations=total,IncomingMessagesProcessed=messages,ConversationsServed=conversations,InputTokens=a?.Input??0,CachedInputTokens=a?.Cached??0,OutputTokens=a?.Output??0,ThinkingTokens=a?.Thinking??0,BillableOutputTokens=a?.Billable??0,EstimatedCostUsd=a?.Cost??0,UnpricedInvocations=a?.Unpriced??0,AverageDurationMs=a?.Duration??0,P95DurationMs=p95,ErrorRate=total==0?0:(double)(a?.Errors??0)/total,AverageInvocationsPerMessage=messages==0?0:(double)total/messages,AverageToolCallsPerMessage=messages==0?0:(double)(a?.Tools??0)/messages,Breakdown=breakdown,Daily=daily };
        return Ok(ApiResponse<WhatsAppAiUsageDto>.SuccessResponse(dto,"Uso de IA obtenido."));
    }
    public static DateTime ToUtc(DateOnly date) => TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Colombia);
    public static int P95Index(int durationCount) => durationCount <= 0 ? 0 : Math.Max(0, (int)Math.Ceiling(durationCount * .95) - 1);
    private static TimeZoneInfo ResolveColombia() { try { return TimeZoneInfo.FindSystemTimeZoneById("America/Bogota"); } catch { return TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time"); } }
}
