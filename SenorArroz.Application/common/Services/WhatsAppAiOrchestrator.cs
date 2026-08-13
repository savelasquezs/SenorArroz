using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Services;

namespace SenorArroz.Application.Common.Services;

public class WhatsAppAiOrchestrator(
    IApplicationDbContext db,
    IWhatsAppAiMessageClaimer claimer,
    IAiChatProviderResolver providers,
    IAiApiKeyProvider apiKeys,
    IAgentToolExecutor tools,
    IWhatsAppAutomaticMessageSender sender,
    IWhatsAppNotificationService notifications,
    IWhatsAppSystemPromptBuilder promptBuilder,
    IWhatsAppSimpleOrderStateService orderState,
    WhatsAppAttentionService attention,
    IClock clock,
    IOptions<WhatsAppAiOrchestratorOptions> options,
    ILogger<WhatsAppAiOrchestrator> logger,
    IOptions<WhatsAppAiPricingOptions>? pricing = null,
    IWhatsAppAiTelemetryQueue? telemetryQueue = null,
    ICurrentTenant? currentTenant = null) : IWhatsAppAiOrchestrator
{
    private readonly WhatsAppAiOrchestratorOptions _options = options.Value;
    private readonly WhatsAppAiPricingOptions _pricing = pricing?.Value ?? new();

    public async Task<WhatsAppAiProcessingResult> ProcessIncomingMessageAsync(
        int conversationId,
        int incomingMessageId,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TotalTimeoutSeconds));
        var ct = timeout.Token;
        var modelCalls = 0;
        var totalTools = 0;
        var executedToolNames = new List<string>();
        string? toolCycleLimitReason = null;
        string? providerName = null;
        string? model = null;
        var maxModelCalls = Math.Clamp(_options.MaxModelCallsPerMessage, 1, 3);
        var maxToolsPerCall = Math.Clamp(_options.MaxToolsPerCall, 1, 1);
        var maxTotalToolCalls = Math.Clamp(_options.MaxTotalToolCalls, 1, 2);

        try
        {
            var claimed = await claimer.TryClaimAsync(conversationId, incomingMessageId, ct);
            if (!claimed)
                return new(false, true, "Mensaje ya procesado, no pendiente o inválido.", false, false, null, null, 0, 0, null);

            var message = await db.WhatsAppMessages
                .AsNoTracking()
                .FirstAsync(x => x.Id == incomingMessageId, ct);
            var conversation = await db.WhatsAppConversations
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == conversationId, ct);

            if (conversation is null)
                return await Ignore(message, "Conversación inexistente.", ct);

            await NotifyCurrentStatusAsync(message.Id, conversation.BranchId, ct);

            if (conversation.AttentionMode != WhatsAppAttentionMode.Ai)
                return await Ignore(message, $"Modo {conversation.AttentionMode}.", ct, conversation.BranchId);
            if (message.Type != WhatsAppMessageType.Text || string.IsNullOrWhiteSpace(message.TextBody))
                return await Ignore(message, "Tipo no soportado.", ct, conversation.BranchId);

            var setting = await db.BranchAiSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.BranchId == conversation.BranchId, ct);
            if (setting is null)
                return await Ignore(message, "IA no configurada.", ct, conversation.BranchId);
            if (!setting.IsActive)
                return await Ignore(message, "Agente de IA deshabilitado.", ct, conversation.BranchId);
            if (!setting.IsVerified)
                return await Ignore(message, "Agente de IA no verificado.", ct, conversation.BranchId);

            providerName = setting.Provider;
            model = setting.Model;
            var apiKey = apiKeys.GetApiKey(setting.Provider);
            if (string.IsNullOrWhiteSpace(apiKey))
                return await RetryOrFail(message, $"Falta la variable de entorno {apiKeys.GetEnvironmentVariableName(setting.Provider)}.", ct, providerName, model, modelCalls, totalTools, conversation.BranchId);
            var provider = providers.Resolve(setting.Provider);
            if (provider is null)
                return await RetryOrFail(message, "Proveedor no soportado.", ct, providerName, model, modelCalls, totalTools, conversation.BranchId);

            if (!string.IsNullOrWhiteSpace(message.AiGeneratedResponse)
                && !string.IsNullOrWhiteSpace(message.AiResponseAttemptId))
            {
                logger.LogInformation(
                    "WhatsApp AI resuming generated response ConversationId={ConversationId} IncomingMessageId={IncomingMessageId} Provider={Provider} Model={Model} AttemptId={AttemptId}",
                    conversationId,
                    incomingMessageId,
                    providerName,
                    model,
                    message.AiResponseAttemptId);

                if (!await StillAi(conversationId, ct))
                    return await Ignore(message, "La atención cambió antes de reanudar el envío.", ct, conversation.BranchId);

                var resumed = await sender.SendTextAsync(
                    conversationId,
                    message.Id,
                    message.AiResponseAttemptId,
                    message.AiGeneratedResponse,
                    ct);
                if (resumed.InProgress)
                {
                    await NotifyCurrentStatusAsync(message.Id, conversation.BranchId, ct);
                    return new(true, false, null, false, false, providerName, model, modelCalls, totalTools, resumed.Error);
                }
                if (resumed.TransientFailure)
                    return await RetryOrFail(message, resumed.Error ?? "Fallo temporal de Meta.", ct, providerName, model, modelCalls, totalTools, conversation.BranchId);

                await Finish(
                    message.Id,
                    resumed.Success ? WhatsAppAiProcessingStatus.Completed : WhatsAppAiProcessingStatus.Failed,
                    resumed.Success ? null : resumed.Error,
                    ct,
                    conversation.BranchId);
                return new(true, false, null, resumed.Success, false, providerName, model, modelCalls, totalTools, resumed.Error);
            }

            var history = await db.WhatsAppMessages
                .AsNoTracking()
                .Where(x =>
                    x.ConversationId == conversationId
                    && x.Id <= incomingMessageId
                    && !string.IsNullOrWhiteSpace(x.TextBody)
                    && x.Type == WhatsAppMessageType.Text
                    && (x.Direction == WhatsAppMessageDirection.Inbound
                        || (x.Direction == WhatsAppMessageDirection.Outbound && x.Status != WhatsAppMessageStatus.Failed)))
                .OrderByDescending(x => x.Timestamp)
                .Take(Math.Max(1, setting.MaxContextMessages))
                .OrderBy(x => x.Timestamp)
                .Select(x => new AiChatMessage(
                    x.Direction == WhatsAppMessageDirection.Inbound ? "user" : "assistant",
                    x.TextBody,
                    null,
                    null))
                .ToListAsync(ct);
            var systemPrompt=await promptBuilder.Build(conversation.BranchId,ct);
            var simpleState=await orderState.LoadAsync(conversationId,ct);
            var cart=await orderState.BuildSummaryAsync(conversation.BranchId,simpleState,ct);
            var customer=conversation.CustomerId.HasValue?await db.Customers.AsNoTracking().Where(x=>x.Id==conversation.CustomerId&&x.BranchId==conversation.BranchId).Select(x=>new
            {
                id=x.Id,
                name=x.Name,
                savedAddresses=x.Addresses
                    .OrderByDescending(a=>a.IsPrimary)
                    .ThenBy(a=>a.Id)
                    .Select(a=>new
                    {
                        id=a.Id,
                        address=a.AddressText,
                        additionalInfo=a.AdditionalInfo,
                        instructions=a.Instructions,
                        neighborhood=a.Neighborhood.Name,
                        deliveryFee=a.DeliveryFee,
                        isPrimary=a.IsPrimary
                    }).ToList()
            }).FirstOrDefaultAsync(ct):null;
            var catalog=await db.Products.AsNoTracking().Include(x=>x.Category).Include(x=>x.CommercialProfile).Where(x=>x.Category.BranchId==conversation.BranchId&&x.Active).OrderBy(x=>x.Name).Select(x=>new{id=x.Id,name=x.Name,price=x.Price,available=!x.Stock.HasValue||x.Stock>0,x.ServesPeopleMin,x.ServesPeopleMax,commercialProfile=x.CommercialProfile==null?null:x.CommercialProfile.Name}).ToListAsync(ct);
            var operationalContext=JsonSerializer.Serialize(new
            {
                architecture="simple_v1",
                customerName=customer?.name,
                savedAddresses=customer?.savedAddresses,
                selectedAddressId=simpleState.SelectedAddressId,
                orderType=simpleState.OrderType?.ToString().ToLowerInvariant(),
                orderActivities=simpleState.Activities.TakeLast(10),
                isFirstAssistantReply=!history.Any(x=>x.Role=="assistant"),
                cart,
                catalog
            },new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var chat=new List<AiChatMessage>{new("system",systemPrompt),new("system",operationalContext)};chat.AddRange(history);

            logger.LogInformation(
                "WhatsApp AI processing ConversationId={ConversationId} IncomingMessageId={IncomingMessageId} Provider={Provider} Model={Model} ContextMessageCount={ContextMessageCount} ToolNames={ToolNames}",
                conversationId,
                incomingMessageId,
                providerName,
                model,
                chat.Count,
                string.Join(",",tools.Definitions.Select(x=>x.Name)));

            while (modelCalls < maxModelCalls)
            {
                if (!await StillAi(conversationId, ct))
                    return await Ignore(message, "La atención cambió durante el procesamiento.", ct, conversation.BranchId);

                modelCalls++;
                var response = await GenerateWithRetry(
                    provider,
                    new(model, apiKey, chat, tools.Definitions, setting.Temperature),
                    conversation.BranchId,
                    conversationId,
                    incomingMessageId,
                    modelCalls,
                    ct);
                if (response.Error is not null)
                {
                    var providerError = FormatProviderError(response);
                    logger.LogError(
                        "WhatsApp AI provider failure ConversationId={ConversationId} IncomingMessageId={IncomingMessageId} Provider={Provider} Model={Model} HttpStatusCode={HttpStatusCode} ProviderError={ProviderError}",
                        conversationId,
                        incomingMessageId,
                        providerName,
                        model,
                        response.HttpStatusCode,
                        response.Error);
                    if(IsConfigurationError(response))
                        return await FailPermanently(message,providerError,ct,providerName,model,modelCalls,totalTools,conversation.BranchId);
                    return await RetryOrFail(message, providerError, ct, providerName, model, modelCalls, totalTools, conversation.BranchId);
                }

                if (response.ToolCalls.Count == 0)
                {
                    if (string.IsNullOrWhiteSpace(response.Text))
                        return await RetryOrFail(message, "Respuesta vacía del proveedor.", ct, providerName, model, modelCalls, totalTools, conversation.BranchId);
                    if (!await StillAi(conversationId, ct))
                        return await Ignore(message, "La atención cambió antes del envío.", ct, conversation.BranchId);

                    var finalText=NormalizeFinalResponse(response.Text);
                    var source = await db.WhatsAppMessages.FirstAsync(x => x.Id == message.Id, ct);
                    source.AiProcessingStatus = WhatsAppAiProcessingStatus.ResponseGenerated;
                    source.AiGeneratedResponse = finalText;
                    source.AiResponseAttemptId ??= Guid.NewGuid().ToString("N");
                    await db.SaveChangesAsync(ct);
                    await NotifyCurrentStatusAsync(source.Id, conversation.BranchId, ct);

                    var sent = await sender.SendTextAsync(
                        conversationId,
                        message.Id,
                        source.AiResponseAttemptId,
                        finalText,
                        ct);
                    if (sent.InProgress)
                    {
                        await NotifyCurrentStatusAsync(message.Id, conversation.BranchId, ct);
                        return new(true, false, null, false, false, providerName, model, modelCalls, totalTools, sent.Error);
                    }
                    if (sent.TransientFailure)
                        return await RetryOrFail(message, sent.Error ?? "Fallo temporal de Meta.", ct, providerName, model, modelCalls, totalTools, conversation.BranchId);

                    await Finish(
                        message.Id,
                        sent.Success ? WhatsAppAiProcessingStatus.Completed : WhatsAppAiProcessingStatus.Failed,
                        sent.Success ? null : sent.Error,
                        ct,
                        conversation.BranchId);
                    logger.LogInformation(
                        "WhatsApp AI completed ConversationId={ConversationId} IncomingMessageId={IncomingMessageId} Provider={Provider} Model={Model} Calls={Calls} Tools={Tools}",
                        conversationId,
                        incomingMessageId,
                        providerName,
                        model,
                        modelCalls,
                        totalTools);
                    return new(true, false, null, sent.Success, false, providerName, model, modelCalls, totalTools, sent.Error);
                }

                if (response.ToolCalls.Count > maxToolsPerCall
                    || totalTools + response.ToolCalls.Count > maxTotalToolCalls)
                {
                    var executed = executedToolNames.Count == 0 ? "ninguna" : string.Join(", ", executedToolNames);
                    var requested = response.ToolCalls.Count == 0 ? "ninguna" : string.Join(", ", response.ToolCalls.Select(x => x.Name));
                    toolCycleLimitReason = $"Se alcanzó el límite seguro del ciclo de herramientas. Ejecutadas: {executed}. Solicitud bloqueada: {requested}.";
                    logger.LogWarning(
                        "WhatsApp AI tool cycle limit ConversationId={ConversationId} IncomingMessageId={IncomingMessageId} ExecutedTools={ExecutedTools} RequestedTools={RequestedTools} MaxTotalTools={MaxTotalTools} MaxToolsPerCall={MaxToolsPerCall}",
                        conversationId,
                        incomingMessageId,
                        executed,
                        requested,
                        maxTotalToolCalls,
                        maxToolsPerCall);
                    break;
                }

                chat.Add(new("assistant", response.Text, null, response.ToolCalls));
                foreach (var call in response.ToolCalls)
                {
                    if (!await StillAi(conversationId, ct))
                        return await Ignore(message, "La atención cambió antes de ejecutar una herramienta.", ct, conversation.BranchId);

                    var result = await tools.ExecuteAsync(
                        call.Name,
                        new(
                            conversationId,
                            conversation.BranchId,
                            incomingMessageId,
                            conversation.PhoneNumber,
                            conversation.CustomerId,
                            null,
                            conversation.AttentionMode.ToString(),
                            message.AiResponseAttemptId ??= $"ai-{incomingMessageId}",
                            "whatsapp_ai"),
                        call.Arguments,
                        ct);
                    totalTools++;
                    executedToolNames.Add(call.Name);
                    if (result.TransferredToHuman)
                    {
                        var transferred = await db.WhatsAppMessages
                            .AsNoTracking()
                            .FirstOrDefaultAsync(x => x.Id == message.Id, ct);
                        var transferReason = transferred?.AiProcessingError
                            ?? result.Message
                            ?? result.Error
                            ?? "La herramienta solicitó atención humana.";
                        var transferWarnings = result.Warnings?
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => x.Trim())
                            .ToList();
                        if (transferWarnings?.Count > 0
                            && !transferReason.Contains("Aviso al cliente no entregado", StringComparison.OrdinalIgnoreCase))
                            transferReason = $"{transferReason} | Aviso al cliente no entregado: {string.Join(" · ", transferWarnings)}";

                        if (transferred?.AiProcessingStatus != WhatsAppAiProcessingStatus.TransferredToHuman)
                            await Finish(message.Id, WhatsAppAiProcessingStatus.TransferredToHuman, transferReason, ct, conversation.BranchId);
                        else
                        {
                            if (transferWarnings?.Count > 0)
                            {
                                var current = await db.WhatsAppMessages.FirstAsync(x => x.Id == message.Id, ct);
                                current.AiProcessingError = transferReason[..Math.Min(1000, transferReason.Length)];
                                current.AiProcessedAt = clock.UtcNow;
                                await db.SaveChangesAsync(ct);
                            }
                            await NotifyCurrentStatusAsync(message.Id, conversation.BranchId, ct);
                        }

                        logger.LogWarning(
                            "WhatsApp AI transferred by tool ConversationId={ConversationId} IncomingMessageId={IncomingMessageId} Provider={Provider} Model={Model} Tool={Tool} TransferReason={TransferReason}",
                            conversationId,
                            incomingMessageId,
                            providerName,
                            model,
                            call.Name,
                            transferReason);
                        return new(true, false, null, false, true, providerName, model, modelCalls, totalTools, transferReason);
                    }

                    var raw = JsonSerializer.Serialize(result);
                    var json = raw.Length <= _options.MaxToolResultCharacters
                        ? raw
                        : JsonSerializer.Serialize(new
                        {
                            success = result.Success,
                            truncated = true,
                            data = raw[..Math.Max(0, _options.MaxToolResultCharacters - 100)]
                        });
                    chat.Add(new("tool", json, call.Id));
                    conversation=await db.WhatsAppConversations.AsNoTracking().FirstAsync(x=>x.Id==conversationId,ct);
                }
            }

            return await Transfer(
                message,
                conversation,
                toolCycleLimitReason
                    ?? $"Se alcanzó el límite seguro del ciclo de herramientas después de {modelCalls} llamada(s) al modelo y {totalTools} herramienta(s): {(executedToolNames.Count == 0 ? "ninguna" : string.Join(", ", executedToolNames))}.",
                ct,
                providerName,
                model,
                modelCalls,
                totalTools);
        }
        catch (OperationCanceledException)
        {
            return await HandleUnexpectedFailure(
                incomingMessageId,
                "Timeout controlado.",
                providerName,
                model,
                modelCalls,
                totalTools,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "WhatsApp AI failed ConversationId={ConversationId} IncomingMessageId={IncomingMessageId} Provider={Provider} Model={Model}",
                conversationId,
                incomingMessageId,
                providerName,
                model);
            return await HandleUnexpectedFailure(
                incomingMessageId,
                $"Error controlado del orquestador: {ex.Message}",
                providerName,
                model,
                modelCalls,
                totalTools,
                CancellationToken.None);
        }
    }

    private async Task<AiChatResponse> GenerateWithRetry(
        IAiChatProvider provider,
        AiChatRequest request,
        int branchId,
        int conversationId,
        int incomingMessageId,
        int invocationIndex,
        CancellationToken cancellationToken)
    {
        AiChatResponse response = new(null, [], request.Model, null, null, null, true, "Sin respuesta");
        for (var attempt = 0; attempt <= _options.TransientRetryCount; attempt++)
        {
            var startedAt = clock.UtcNow;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                response = await provider.GenerateAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                EnqueueInvocation(provider.ProviderName, request.Model, branchId, conversationId, incomingMessageId, invocationIndex, attempt + 1, startedAt, stopwatch.ElapsedMilliseconds, request, new(null, [], request.Model, null, null, null, false, SanitizeError(ex.Message)), "unexpected_exception");
                throw;
            }
            stopwatch.Stop();
            EnqueueInvocation(provider.ProviderName, request.Model, branchId, conversationId, incomingMessageId, invocationIndex, attempt + 1, startedAt, stopwatch.ElapsedMilliseconds, request, response);
            if (!response.IsTransientError)
                return response;
            if (attempt < _options.TransientRetryCount)
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), cancellationToken);
        }
        return response;
    }

    private void EnqueueInvocation(string provider, string model, int branchId, int conversationId, int messageId, int invocationIndex, int attemptIndex, DateTime startedAt, long durationMs, AiChatRequest request, AiChatResponse response, string? category = null)
    {
        var price = _pricing.Find(provider, model);
        var usage = AiBillingUsage.From(provider, response);
        decimal? cost = null;
        if (price is not null && response.InputTokens.HasValue && response.OutputTokens.HasValue)
            cost = (usage.UncachedInputTokens * price.InputPerMillionUsd + usage.CachedInputTokens * price.CachedInputPerMillionUsd + usage.BillableOutputTokens * price.OutputPerMillionUsd) / 1_000_000m;
        var error = SanitizeError(response.Error);
        var entity = new WhatsAppAiInvocation
        {
            TenantId = currentTenant?.HasTenant == true ? currentTenant.TenantId : null,
            BranchId = branchId, ConversationId = conversationId, IncomingMessageId = messageId,
            Provider = provider, Model = model, InvocationIndex = invocationIndex, AttemptIndex = attemptIndex,
            StartedAt = startedAt, CompletedAt = startedAt.AddMilliseconds(durationMs), DurationMs = durationMs,
            InputTokens = response.InputTokens, CachedInputTokens = response.CachedInputTokens, OutputTokens = response.OutputTokens, ThinkingTokens = response.ThinkingTokens, BillableOutputTokens = usage.BillableOutputTokens,
            ToolCallCount = response.ToolCalls.Count, FinishReason = response.FinishReason,
            Success = response.Error is null, IsTransientError = response.IsTransientError, HttpStatusCode = response.HttpStatusCode,
            ErrorCategory = category ?? (response.Error is null ? null : IsConfigurationError(response) ? "configuration_error" : response.HttpStatusCode.HasValue ? "http" : response.IsTransientError ? "transient_technical" : "provider"),
            ErrorMessage = error,
            InputPricePerMillionUsd = price?.InputPerMillionUsd, CachedInputPricePerMillionUsd = price?.CachedInputPerMillionUsd, OutputPricePerMillionUsd = price?.OutputPerMillionUsd,
            EstimatedCostUsd = cost, PricingEffectiveDate = price is null ? null : _pricing.EffectiveDate, CreatedAt = clock.UtcNow
        };
        var systemMessages=request.Messages.Where(x=>x.Role=="system").ToList();
        entity.ContextStrategy="simple_v1";entity.ContextMessageCount=request.Messages.Count(x=>x.Role!="system");entity.ToolDefinitionCount=request.Tools.Count;entity.SystemPromptCharacters=systemMessages.FirstOrDefault()?.Content?.Length??0;entity.RuntimeContextCharacters=systemMessages.Skip(1).FirstOrDefault()?.Content?.Length??0;entity.HistoryCharacters=request.Messages.Where(x=>x.Role!="system").Sum(x=>x.Content?.Length??0);entity.ToolDefinitionsCharacters=request.Tools.Sum(x=>x.Name.Length+x.Description.Length+x.ParametersSchema.GetRawText().Length);entity.ContextPlannerFallback=false;entity.ContextPlannerFallbackReason=null;
        if (telemetryQueue is null || !telemetryQueue.TryEnqueue(entity))
            logger.LogWarning("WhatsApp AI invocation telemetry was not queued Provider={Provider} Model={Model} MessageId={MessageId}", provider, model, messageId);
    }

    private static string? SanitizeError(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var sanitized = System.Text.RegularExpressions.Regex.Replace(value, "(?i)(api[-_ ]?key|bearer|token)\\s*[:=]?\\s*[^\\s,;]+", "$1=[redacted]");
        return sanitized[..Math.Min(500, sanitized.Length)];
    }

    private Task<bool> StillAi(int conversationId, CancellationToken cancellationToken) =>
        db.WhatsAppConversations
            .AsNoTracking()
            .AnyAsync(x => x.Id == conversationId && x.AttentionMode == WhatsAppAttentionMode.Ai, cancellationToken);

    private static string NormalizeFinalResponse(string value)
    {
        var normalized=System.Text.RegularExpressions.Regex.Replace(value.Trim(),@"\s+"," ");
        return normalized[..Math.Min(500,normalized.Length)].TrimEnd();
    }

    private async Task<WhatsAppAiProcessingResult> HandleUnexpectedFailure(
        int messageId,
        string error,
        string? provider,
        string? model,
        int calls,
        int toolsUsed,
        CancellationToken cancellationToken)
    {
        var current = await db.WhatsAppMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == messageId, cancellationToken);
        if (current is null)
            return new(true, false, null, false, false, provider, model, calls, toolsUsed, error);

        var branchId = await GetBranchId(current.ConversationId, cancellationToken);
        if (current.AiProcessingStatus == WhatsAppAiProcessingStatus.TransferredToHuman)
        {
            await NotifyCurrentStatusAsync(messageId, branchId, cancellationToken);
            return new(true, false, null, false, true, provider, model, calls, toolsUsed, current.AiProcessingError);
        }
        if (current.AiProcessingStatus == WhatsAppAiProcessingStatus.Sent
            || !string.IsNullOrWhiteSpace(current.AiResponseWhatsAppMessageId))
        {
            await Finish(messageId, WhatsAppAiProcessingStatus.Completed, null, cancellationToken, branchId);
            return new(true, false, null, true, false, provider, model, calls, toolsUsed, null);
        }
        if (current.AiProcessingStatus == WhatsAppAiProcessingStatus.Sending)
        {
            var uncertain = $"{error} El resultado del POST a Meta es desconocido; no se reintentó para evitar duplicados.";
            await Finish(messageId, WhatsAppAiProcessingStatus.Failed, uncertain, cancellationToken, branchId);
            return new(true, false, null, false, false, provider, model, calls, toolsUsed, uncertain);
        }

        return await RetryOrFail(current, error, cancellationToken, provider, model, calls, toolsUsed, branchId);
    }

    private async Task<WhatsAppAiProcessingResult> Ignore(
        WhatsAppMessage message,
        string reason,
        CancellationToken cancellationToken,
        int? branchId = null)
    {
        await Finish(message.Id, WhatsAppAiProcessingStatus.Ignored, reason, cancellationToken, branchId);
        return new(false, true, reason, false, false, null, null, 0, 0, null);
    }

    private async Task<WhatsAppAiProcessingResult> RetryOrFail(
        WhatsAppMessage message,
        string error,
        CancellationToken cancellationToken,
        string? provider,
        string? model,
        int calls,
        int toolsUsed,
        int? branchId = null)
    {
        var current = await db.WhatsAppMessages.FirstAsync(x => x.Id == message.Id, cancellationToken);
        var exhausted = current.AiProcessingAttempts >= _options.MaxPersistentAttempts;
        current.AiProcessingStatus = exhausted
            ? WhatsAppAiProcessingStatus.Failed
            : WhatsAppAiProcessingStatus.Pending;
        current.AiProcessingStartedAt = null;
        current.AiNextRetryAt = exhausted
            ? null
            : clock.UtcNow.AddSeconds(Math.Pow(2, current.AiProcessingAttempts) * 5);
        current.AiProcessingError = error[..Math.Min(1000, error.Length)];
        // Preserve when this attempt failed even if another retry is scheduled;
        // diagnostics use it as the operational status-change timestamp.
        current.AiProcessedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        branchId ??= await GetBranchId(current.ConversationId, cancellationToken);
        await NotifyCurrentStatusAsync(current.Id, branchId, cancellationToken);
        logger.LogWarning(
            "WhatsApp AI technical failure ConversationId={ConversationId} IncomingMessageId={IncomingMessageId} Provider={Provider} Model={Model} ProviderError={ProviderError} Exhausted={Exhausted} Attempt={Attempt} MaxAttempts={MaxAttempts} NextRetryAt={NextRetryAt}",
            current.ConversationId,
            current.Id,
            provider,
            model,
            error,
            exhausted,
            current.AiProcessingAttempts,
            _options.MaxPersistentAttempts,
            current.AiNextRetryAt);
        return new(true, false, null, false, false, provider, model, calls, toolsUsed, error);
    }

    private async Task<WhatsAppAiProcessingResult> FailPermanently(WhatsAppMessage message,string error,CancellationToken cancellationToken,string? provider,string? model,int calls,int toolsUsed,int? branchId)
    {
        var current=await db.WhatsAppMessages.FirstAsync(x=>x.Id==message.Id,cancellationToken);
        current.AiProcessingStatus=WhatsAppAiProcessingStatus.Failed;
        current.AiProcessingStartedAt=null;
        current.AiNextRetryAt=null;
        current.AiProcessingError=error[..Math.Min(1000,error.Length)];
        current.AiProcessedAt=clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await NotifyCurrentStatusAsync(current.Id,branchId??await GetBranchId(current.ConversationId,cancellationToken),cancellationToken);
        logger.LogError("WhatsApp AI configuration failure ConversationId={ConversationId} IncomingMessageId={IncomingMessageId} Provider={Provider} Model={Model} Error={Error}",current.ConversationId,current.Id,provider,model,error);
        return new(true,false,null,false,false,provider,model,calls,toolsUsed,error);
    }

    private static bool IsConfigurationError(AiChatResponse response)
    {
        if(response.HttpStatusCode==400)return true;
        var error=response.Error??string.Empty;
        return !response.IsTransientError&&(error.Contains("Herramienta '",StringComparison.OrdinalIgnoreCase)
            || response.HttpStatusCode is 404 or 422&&System.Text.RegularExpressions.Regex.IsMatch(error,"(?i)(model|modelo|schema|function|tool|argument|malformed|invalid)"));
    }

    private async Task<WhatsAppAiProcessingResult> Transfer(
        WhatsAppMessage message,
        WhatsAppConversation snapshot,
        string reason,
        CancellationToken cancellationToken,
        string? provider,
        string? model,
        int calls,
        int toolsUsed)
    {
        var conversation = await db.WhatsAppConversations
            .FirstAsync(x => x.Id == snapshot.Id, cancellationToken);
        var changed = conversation.AttentionMode == WhatsAppAttentionMode.Ai
            && attention.RequestHuman(conversation, null, clock.UtcNow);
        var current = await db.WhatsAppMessages
            .FirstAsync(x => x.Id == message.Id, cancellationToken);
        current.AiProcessingStatus = WhatsAppAiProcessingStatus.TransferredToHuman;
        current.AiProcessingError = reason[..Math.Min(1000, reason.Length)];
        current.AiProcessedAt = clock.UtcNow;
        current.AiProcessingStartedAt = null;
        current.AiNextRetryAt = null;
        await db.SaveChangesAsync(cancellationToken);
        await NotifyCurrentStatusAsync(current.Id, conversation.BranchId, cancellationToken);

        logger.LogWarning(
            "WhatsApp AI transfer ConversationId={ConversationId} IncomingMessageId={IncomingMessageId} Provider={Provider} Model={Model} TransferReason={TransferReason} AttentionChanged={AttentionChanged}",
            conversation.Id,
            current.Id,
            provider,
            model,
            reason,
            changed);

        if (changed)
        {
            try
            {
                await notifications.NotifyAttentionChangedAsync(
                    conversation.BranchId,
                    new WhatsAppConversationDto
                    {
                        Id = conversation.Id,
                        BranchId = conversation.BranchId,
                        PhoneNumber = conversation.PhoneNumber,
                        WhatsAppUsername = conversation.WhatsAppUsername,
                        HasWhatsAppIdentity = !string.IsNullOrWhiteSpace(conversation.WhatsAppUserId),
                        Status = "open",
                        AttentionMode = "waitingForHuman",
                        LastMessageAt = conversation.LastMessageAt,
                        LastMessagePreview = conversation.LastMessagePreview,
                        UnreadCount = conversation.UnreadCount,
                        CreatedAt = conversation.CreatedAt,
                        UpdatedAt = conversation.UpdatedAt,
                        AttentionModeUpdatedAt = conversation.AttentionModeUpdatedAt,
                        AiPausedAt = conversation.AiPausedAt
                    },
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Could not emit WhatsApp attention transfer ConversationId={ConversationId} IncomingMessageId={IncomingMessageId}",
                    conversation.Id,
                    current.Id);
            }

            var configured = await db.BranchAiSettings
                .AsNoTracking()
                .Where(x => x.BranchId == conversation.BranchId)
                .Select(x => x.TransferMessage)
                .FirstOrDefaultAsync(cancellationToken);
            var text = string.IsNullOrWhiteSpace(configured)
                ? "Un asesor continuará con tu atención."
                : configured.Trim();
            WhatsAppAutomaticSendResult transferSend;
            try
            {
                transferSend = await sender.SendTransferTextAsync(
                    conversation.Id,
                    current.Id,
                    current.AiResponseAttemptId ??= $"transfer-{current.Id}",
                    text,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "WhatsApp AI transfer notice failed unexpectedly ConversationId={ConversationId} IncomingMessageId={IncomingMessageId}",
                    conversation.Id,
                    current.Id);
                transferSend = new(false, false, null, $"Fallo inesperado al enviar por Meta: {ex.Message}");
            }
            if (!transferSend.Success)
            {
                var deliveryError = string.IsNullOrWhiteSpace(transferSend.Error)
                    ? "Meta no confirmó el envío."
                    : transferSend.Error.Trim();
                var fullReason = $"{reason} | Aviso al cliente no entregado: {deliveryError}";
                current.AiProcessingError = fullReason[..Math.Min(1000, fullReason.Length)];
                current.AiProcessedAt = clock.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                await NotifyCurrentStatusAsync(current.Id, conversation.BranchId, cancellationToken);
                logger.LogError(
                    "WhatsApp AI transfer notice failed ConversationId={ConversationId} IncomingMessageId={IncomingMessageId} Provider={Provider} Model={Model} TransferReason={TransferReason} MetaError={MetaError}",
                    conversation.Id,
                    current.Id,
                    provider,
                    model,
                    reason,
                    deliveryError);
                reason = fullReason;
            }
        }

        return new(true, false, null, false, changed, provider, model, calls, toolsUsed, reason);
    }

    private async Task Finish(
        int messageId,
        WhatsAppAiProcessingStatus status,
        string? error,
        CancellationToken cancellationToken,
        int? branchId = null)
    {
        if (status == WhatsAppAiProcessingStatus.Completed)
        {
            var snapshot = await db.WhatsAppMessages
                .AsNoTracking()
                .Where(x => x.Id == messageId)
                .Select(x => new { x.ConversationId })
                .FirstOrDefaultAsync(cancellationToken);
            if (snapshot is null)
                return;

            // A Meta delivery-failure webhook may arrive between the send and this
            // finalization. Complete with a conditional write so a stale tracked
            // entity can never erase Failed or TransferredToHuman.
            await claimer.TryCompleteAsync(
                snapshot.ConversationId,
                messageId,
                clock.UtcNow,
                cancellationToken);
            branchId ??= await GetBranchId(snapshot.ConversationId, cancellationToken);
            await NotifyCurrentStatusAsync(messageId, branchId, cancellationToken);
            return;
        }

        var message = await db.WhatsAppMessages
            .FirstOrDefaultAsync(x => x.Id == messageId, cancellationToken);
        if (message is null)
            return;

        message.AiProcessingStatus = status;
        message.AiProcessedAt = clock.UtcNow;
        message.AiProcessingError = error;
        message.AiProcessingStartedAt = null;
        message.AiNextRetryAt = null;
        await db.SaveChangesAsync(cancellationToken);

        branchId ??= await GetBranchId(message.ConversationId, cancellationToken);
        await NotifyCurrentStatusAsync(message.Id, branchId, cancellationToken);
    }

    private async Task<int?> GetBranchId(int conversationId, CancellationToken cancellationToken) =>
        await db.WhatsAppConversations
            .AsNoTracking()
            .Where(x => x.Id == conversationId)
            .Select(x => (int?)x.BranchId)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task NotifyCurrentStatusAsync(
        int messageId,
        int? branchId,
        CancellationToken cancellationToken)
    {
        if (!branchId.HasValue)
            return;

        try
        {
            var message = await db.WhatsAppMessages
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == messageId, cancellationToken);
            if (message is null)
                return;

            await notifications.NotifyAiProcessingChangedAsync(
                branchId.Value,
                WhatsAppAiDiagnosticsMapper.ToDto(message, _options.MaxPersistentAttempts),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The processing cancellation is handled by the orchestrator; diagnostics are best effort.
        }
        catch (Exception ex)
        {
            // Operational feedback must never make customer-facing processing fail.
            logger.LogWarning(
                ex,
                "Could not emit WhatsApp AI processing update BranchId={BranchId} IncomingMessageId={IncomingMessageId}",
                branchId,
                messageId);
        }
    }

    private static string FormatProviderError(AiChatResponse response) => response.HttpStatusCode.HasValue
        ? $"HTTP {response.HttpStatusCode.Value}: {response.Error}"
        : response.Error ?? "Error desconocido del proveedor.";
}
