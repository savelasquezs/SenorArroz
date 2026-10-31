using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.API.Services;

public sealed record WhatsAppInternalToolRequest(string Name, int MessageId, string ExecutionId, JsonElement Arguments,
    DateTime? AttentionVersion = null);

public sealed class WhatsAppBusinessGateway(HttpClient http, IAgentToolCatalog catalog, AgentToolExecutor local,
    IEnumerable<IAgentTool> registry,
    IOptions<WhatsAppProcessingOptions> options) : IAgentToolExecutor
{
    public IReadOnlyList<AiToolDefinition> Definitions => catalog.All;

    public async Task<bool> RetryPaymentAsync(int messageId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(new Uri(options.Value.BusinessApiUrl.TrimEnd('/') + "/"), "internal/whatsapp/payment-retry"));
        request.Headers.Add("X-WhatsApp-Worker-Key", options.Value.WorkerKey);
        request.Content = JsonContent.Create(new WhatsAppInternalToolRequest("retry_payment", messageId,
            $"payment-retry:{messageId}", JsonSerializer.SerializeToElement(new { })));
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Business payment retry was not confirmed.");
        return await response.Content.ReadFromJsonAsync<bool>(cancellationToken: ct);
    }

    public static bool IsAuthorized(HttpContext http, WhatsAppProcessingOptions options)
    {
        var supplied = http.Request.Headers["X-WhatsApp-Worker-Key"].ToString();
        return options.External && options.WorkerKey.Length is >= 32 and <= 256 && supplied.Length <= 256
            && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(options.WorkerKey)),
                SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(string name, AgentToolExecutionContext context,
        JsonElement arguments, CancellationToken ct)
    {
        var tool = registry.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (tool is not null && (!tool.ModifiesData || tool.Category == "messaging"))
            return await local.ExecuteAsync(name, context, arguments, ct);
        if (!context.IncomingMessageId.HasValue || string.IsNullOrWhiteSpace(context.ExecutionId))
            return new(false, null, "Falta el contexto del mensaje persistido.", "invalid_worker_context");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(new Uri(options.Value.BusinessApiUrl.TrimEnd('/') + "/"), "internal/whatsapp/tools"));
        request.Headers.Add("X-WhatsApp-Worker-Key", options.Value.WorkerKey);
        request.Content = JsonContent.Create(new WhatsAppInternalToolRequest(name,
            context.IncomingMessageId.Value, context.ExecutionId, arguments, context.AttentionVersion));
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return new(false, null, "La API de negocio no confirmó la acción.", "business_gateway_rejected");
            return await response.Content.ReadFromJsonAsync<AgentToolExecutionResult>(cancellationToken: ct)
                ?? new(false, null, "La API devolvió una respuesta inválida.", "invalid_business_response");
        }
        catch (HttpRequestException)
        {
            return new(false, null, "Resultado comercial no confirmado; requiere revisión.", "business_result_unknown");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, null, "Resultado comercial no confirmado; requiere revisión.", "business_result_unknown");
        }
    }

    public static async Task<IResult> HandleAsync(HttpContext http, WhatsAppInternalToolRequest request,
        IOptions<WhatsAppProcessingOptions> options, ApplicationDbContext db, ITenantExecutionContext execution,
        IAgentToolExecutor tools, CancellationToken ct)
    {
        if (!IsAuthorized(http, options.Value)) return Results.Unauthorized();
        if (request.MessageId <= 0 || string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 80
            || string.IsNullOrWhiteSpace(request.ExecutionId)
            || request.ExecutionId.Length > 180 || request.Arguments.ValueKind != JsonValueKind.Object)
            return Results.BadRequest();
        int tenantId;
        using (execution.BeginSystemScope())
            tenantId = await db.WhatsAppMessages.AsNoTracking().Where(x => x.Id == request.MessageId
                    && x.Direction == WhatsAppMessageDirection.Inbound && x.AiProcessingStatus == WhatsAppAiProcessingStatus.Processing)
                .Select(x => x.TenantId).SingleOrDefaultAsync(ct);
        if (tenantId <= 0) return Results.NotFound();
        using var tenant = execution.BeginTenantScope(tenantId);
        var source = await db.WhatsAppMessages.AsNoTracking().Include(x => x.Conversation)
            .SingleAsync(x => x.Id == request.MessageId, ct);
        if (source.Conversation.AttentionMode != WhatsAppAttentionMode.Ai || !request.AttentionVersion.HasValue
            || source.Conversation.AttentionModeUpdatedAt != request.AttentionVersion.Value) return Results.Conflict();
        var context = new AgentToolExecutionContext(source.ConversationId,
            source.Conversation.OperationalBranchId ?? source.Conversation.BranchId,
            source.Id, ExecutionId: request.ExecutionId, TechnicalActor: "whatsapp-worker");
        return Results.Ok(await tools.ExecuteAsync(request.Name, context, request.Arguments, ct));
    }

    public static async Task<IResult> HandlePaymentRetryAsync(HttpContext http, WhatsAppInternalToolRequest request,
        IOptions<WhatsAppProcessingOptions> options, ApplicationDbContext db, ITenantExecutionContext execution,
        WhatsAppCommerceFlowService commerce, CancellationToken ct)
    {
        if (!IsAuthorized(http, options.Value)) return Results.Unauthorized();
        int tenantId;
        using (execution.BeginSystemScope())
            tenantId = await db.WhatsAppMessages.AsNoTracking().Where(x => x.Id == request.MessageId
                && x.Direction == WhatsAppMessageDirection.Inbound && x.Conversation.ChannelSettingId != null
                && db.WhatsAppWebhookEvents.Any(e => e.WhatsAppMessageId == x.WhatsAppMessageId
                    && e.ProcessingState == "processing" && e.LeaseUntil > DateTime.UtcNow))
                .Select(x => x.TenantId).SingleOrDefaultAsync(ct);
        if (tenantId <= 0) return Results.NotFound();
        using var tenant = execution.BeginTenantScope(tenantId);
        var source = await db.WhatsAppMessages.AsNoTracking().SingleAsync(x => x.Id == request.MessageId, ct);
        return Results.Ok(await commerce.RetryPaymentAsync(source.ConversationId, source.Id, ct));
    }
}
