using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.API.Services;

public record WhatsAppAiWorkItem(int ConversationId, int MessageId);

public class WhatsAppAiWorkQueue : IWhatsAppAiWorkQueue
{
    private const int Capacity = 500;
    private readonly ConcurrentDictionary<WhatsAppAiWorkItem, byte> _scheduled = new();
    private readonly Channel<WhatsAppAiWorkItem> _channel = Channel.CreateBounded<WhatsAppAiWorkItem>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    public bool TryEnqueue(int conversationId, int messageId)
    {
        var item = new WhatsAppAiWorkItem(conversationId, messageId);
        if (!_scheduled.TryAdd(item, 0))
        {
            return false;
        }

        if (_channel.Writer.TryWrite(item))
        {
            return true;
        }

        // A full queue must not leave a phantom reservation that prevents a later retry.
        _scheduled.TryRemove(item, out _);
        return false;
    }

    public IAsyncEnumerable<WhatsAppAiWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void MarkCompleted(WhatsAppAiWorkItem item) => _scheduled.TryRemove(item, out _);
}

public class WhatsAppAiBackgroundService(
    WhatsAppAiWorkQueue queue,
    IServiceScopeFactory scopes,
    ILogger<WhatsAppAiBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider
                    .GetRequiredService<IWhatsAppAiOrchestrator>()
                    .ProcessIncomingMessageAsync(item.ConversationId, item.MessageId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "WhatsApp AI background item failed ConversationId={ConversationId} MessageId={MessageId}",
                    item.ConversationId,
                    item.MessageId);
            }
            finally
            {
                // Keep the reservation during execution so recovery cycles cannot enqueue the item twice.
                queue.MarkCompleted(item);
            }
        }
    }
}

public class WhatsAppAiRecoveryService(
    WhatsAppAiWorkQueue queue,
    IServiceScopeFactory scopes,
    IOptions<WhatsAppAiOrchestratorOptions> options,
    ILogger<WhatsAppAiRecoveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var recoveryInterval = TimeSpan.FromSeconds(Math.Max(1, options.Value.RecoveryIntervalSeconds));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RecoverOnceAsync(cancellationToken);
                await Task.Delay(recoveryInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "WhatsApp AI recovery cycle failed.");
                await Task.Delay(recoveryInterval, cancellationToken);
            }
        }
    }

    protected virtual async Task RecoverOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var configuration = options.Value;
        var now = DateTime.UtcNow;
        var staleBefore = now.AddSeconds(-Math.Max(1, configuration.ProcessingStaleAfterSeconds));

        var interrupted = await db.WhatsAppMessages
            .Where(message =>
                message.Direction == WhatsAppMessageDirection.Inbound &&
                (message.AiProcessingStatus == WhatsAppAiProcessingStatus.Processing ||
                 message.AiProcessingStatus == WhatsAppAiProcessingStatus.ResponseGenerated ||
                 message.AiProcessingStatus == WhatsAppAiProcessingStatus.Sending ||
                 message.AiProcessingStatus == WhatsAppAiProcessingStatus.Sent) &&
                (message.AiProcessingStartedAt == null || message.AiProcessingStartedAt < staleBefore) &&
                (message.AiNextRetryAt == null || message.AiNextRetryAt <= now))
            .OrderBy(message => message.Id)
            .Take(Math.Max(1, configuration.RecoveryBatchSize))
            .ToListAsync(cancellationToken);

        var potentiallySent = interrupted
            .Where(message =>
                message.AiProcessingStatus == WhatsAppAiProcessingStatus.Sending ||
                message.AiProcessingStatus == WhatsAppAiProcessingStatus.Sent)
            .ToList();
        var sentAttempts = await LoadSentAttemptsAsync(db, potentiallySent, cancellationToken);

        foreach (var message in interrupted)
        {
            switch (message.AiProcessingStatus)
            {
                case WhatsAppAiProcessingStatus.Processing:
                    if (message.AiProcessingAttempts >= configuration.MaxPersistentAttempts)
                    {
                        message.AiProcessingStatus = WhatsAppAiProcessingStatus.Failed;
                        message.AiProcessingStartedAt = null;
                        message.AiNextRetryAt = null;
                        message.AiProcessedAt = now;
                        message.AiProcessingError = "El procesamiento interrumpido agotó el máximo de intentos.";
                    }
                    else ResetForSafeRetry(message, now, "Se recuperó un procesamiento interrumpido.");
                    break;

                case WhatsAppAiProcessingStatus.ResponseGenerated:
                    // No external send started in this state. Re-enter through Pending so the atomic
                    // message claimer remains the cross-process idempotency boundary.
                    ResetForSafeRetry(message, now, "Se recuperó una respuesta generada antes del envío.");
                    break;

                case WhatsAppAiProcessingStatus.Sending:
                    ReconcileUncertainSend(message, sentAttempts, now);
                    break;

                case WhatsAppAiProcessingStatus.Sent:
                    ReconcileSent(message, sentAttempts, now);
                    break;
            }
        }

        if (interrupted.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var dueMessages = await db.WhatsAppMessages
            .Where(message =>
                message.Direction == WhatsAppMessageDirection.Inbound &&
                message.AiProcessingStatus == WhatsAppAiProcessingStatus.Pending &&
                (message.AiNextRetryAt == null || message.AiNextRetryAt <= now))
            .OrderBy(message => message.Id)
            .Take(Math.Max(1, configuration.RecoveryBatchSize))
            .ToListAsync(cancellationToken);

        foreach (var exhausted in dueMessages.Where(message => message.AiProcessingAttempts >= configuration.MaxPersistentAttempts))
        {
            exhausted.AiProcessingStatus = WhatsAppAiProcessingStatus.Failed;
            exhausted.AiNextRetryAt = null;
            exhausted.AiProcessedAt = now;
            exhausted.AiProcessingError = "El mensaje agotó el máximo de intentos antes de ser reclamado.";
        }
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(cancellationToken);

        foreach (var message in dueMessages.Where(message => message.AiProcessingAttempts < configuration.MaxPersistentAttempts))
        {
            // False may mean either "already scheduled" or "currently full". In both cases it is
            // important to continue so one duplicate cannot hide later recoverable messages.
            queue.TryEnqueue(message.ConversationId, message.Id);
        }
    }

    private static void ResetForSafeRetry(WhatsAppMessage message, DateTime now, string reason)
    {
        message.AiProcessingStatus = WhatsAppAiProcessingStatus.Pending;
        message.AiProcessingStartedAt = null;
        message.AiNextRetryAt = now;
        message.AiProcessingError = reason;
        message.AiProcessedAt = null;
    }

    private static void ReconcileUncertainSend(
        WhatsAppMessage message,
        IReadOnlyDictionary<string, string?> sentAttempts,
        DateTime now)
    {
        if (!string.IsNullOrWhiteSpace(message.AiResponseWhatsAppMessageId))
        {
            CompleteAlreadySent(message, now);
            return;
        }

        if (!string.IsNullOrWhiteSpace(message.AiResponseAttemptId) &&
            sentAttempts.TryGetValue(message.AiResponseAttemptId, out var whatsAppMessageId) &&
            !string.IsNullOrWhiteSpace(whatsAppMessageId))
        {
            message.AiResponseWhatsAppMessageId = whatsAppMessageId;
            CompleteAlreadySent(message, now);
            return;
        }

        // Meta has no client idempotency key for /messages. Retrying an interrupted Sending state
        // could deliver the same answer twice, so the uncertainty is made explicit and terminal.
        message.AiProcessingStatus = WhatsAppAiProcessingStatus.Failed;
        message.AiProcessingStartedAt = null;
        message.AiNextRetryAt = null;
        message.AiProcessedAt = now;
        message.AiProcessingError =
            "Envío interrumpido sin confirmación de Meta; no se reintentó para evitar un mensaje duplicado.";
    }

    private static void ReconcileSent(
        WhatsAppMessage message,
        IReadOnlyDictionary<string, string?> sentAttempts,
        DateTime now)
    {
        if (!string.IsNullOrWhiteSpace(message.AiResponseWhatsAppMessageId))
        {
            CompleteAlreadySent(message, now);
            return;
        }

        if (!string.IsNullOrWhiteSpace(message.AiResponseAttemptId) &&
            sentAttempts.TryGetValue(message.AiResponseAttemptId, out var whatsAppMessageId) &&
            !string.IsNullOrWhiteSpace(whatsAppMessageId))
        {
            message.AiResponseWhatsAppMessageId = whatsAppMessageId;
            CompleteAlreadySent(message, now);
            return;
        }

        message.AiProcessingStatus = WhatsAppAiProcessingStatus.Failed;
        message.AiProcessingStartedAt = null;
        message.AiNextRetryAt = null;
        message.AiProcessedAt = now;
        message.AiProcessingError =
            "Estado Sent sin identificador de WhatsApp; no se reintentó para evitar un mensaje duplicado.";
    }

    private static void CompleteAlreadySent(WhatsAppMessage message, DateTime now)
    {
        message.AiProcessingStatus = WhatsAppAiProcessingStatus.Completed;
        message.AiProcessingStartedAt = null;
        message.AiNextRetryAt = null;
        message.AiProcessedAt = now;
        message.AiProcessingError = null;
    }

    private static async Task<IReadOnlyDictionary<string, string?>> LoadSentAttemptsAsync(
        ApplicationDbContext db,
        IReadOnlyCollection<WhatsAppMessage> potentiallySent,
        CancellationToken cancellationToken)
    {
        var attemptIds = potentiallySent
            .Select(message => message.AiResponseAttemptId)
            .Where(attemptId => !string.IsNullOrWhiteSpace(attemptId))
            .ToHashSet(StringComparer.Ordinal);
        if (attemptIds.Count == 0)
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        var conversationIds = potentiallySent.Select(message => message.ConversationId).Distinct().ToList();
        var candidates = await db.WhatsAppMessages
            .AsNoTracking()
            .Where(message =>
                conversationIds.Contains(message.ConversationId) &&
                message.Direction == WhatsAppMessageDirection.Outbound &&
                message.SentByAi &&
                message.Status == WhatsAppMessageStatus.Sent &&
                message.RawPayload != null)
            .Select(message => new { message.RawPayload, message.WhatsAppMessageId })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (TryReadAiAttemptId(candidate.RawPayload, out var attemptId) &&
                attemptIds.Contains(attemptId))
            {
                result.TryAdd(attemptId, candidate.WhatsAppMessageId);
            }
        }

        return result;
    }

    private static bool TryReadAiAttemptId(string? rawPayload, out string attemptId)
    {
        attemptId = string.Empty;
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            var root = document.RootElement;
            if (!root.TryGetProperty("origin", out var origin) ||
                !string.Equals(origin.GetString(), "ai", StringComparison.Ordinal) ||
                !root.TryGetProperty("attemptId", out var attempt))
            {
                return false;
            }

            attemptId = attempt.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(attemptId);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
