using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
namespace SenorArroz.Infrastructure.Services;
public class WhatsAppAiMessageClaimer(
    ApplicationDbContext db,
    IOptions<WhatsAppAiOrchestratorOptions> options) : IWhatsAppAiMessageClaimer
{
    public async Task<bool> TryClaimAsync(int conversationId, int messageId, CancellationToken ct) =>
        await db.WhatsAppMessages
            .Where(x =>
                x.Id == messageId
                && x.ConversationId == conversationId
                && x.Direction == WhatsAppMessageDirection.Inbound
                && x.AiProcessingStatus == WhatsAppAiProcessingStatus.Pending
                && x.AiProcessingAttempts < options.Value.MaxPersistentAttempts
                && (x.AiNextRetryAt == null || x.AiNextRetryAt <= DateTime.UtcNow))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.AiProcessingStatus, WhatsAppAiProcessingStatus.Processing)
                    .SetProperty(x => x.AiProcessingStartedAt, DateTime.UtcNow)
                    .SetProperty(x => x.AiNextRetryAt, (DateTime?)null)
                    .SetProperty(x => x.AiProcessingAttempts, x => x.AiProcessingAttempts + 1),
                ct) == 1;

    public async Task<bool> TryCompleteAsync(
        int conversationId,
        int messageId,
        DateTime processedAt,
        CancellationToken ct)
    {
        var query = db.WhatsAppMessages.Where(x =>
            x.Id == messageId
            && x.ConversationId == conversationId
            && x.Direction == WhatsAppMessageDirection.Inbound
            && x.AiProcessingStatus != WhatsAppAiProcessingStatus.Failed
            && x.AiProcessingStatus != WhatsAppAiProcessingStatus.TransferredToHuman);

        if (db.Database.IsRelational())
        {
            return await query.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.AiProcessingStatus, WhatsAppAiProcessingStatus.Completed)
                    .SetProperty(x => x.AiProcessedAt, processedAt)
                    .SetProperty(x => x.AiProcessingError, (string?)null)
                    .SetProperty(x => x.AiProcessingStartedAt, (DateTime?)null)
                    .SetProperty(x => x.AiNextRetryAt, (DateTime?)null),
                ct) == 1;
        }

        // ExecuteUpdate is relational-only. Keep the in-memory path for tests while
        // deliberately discarding any stale tracked snapshot first.
        db.ChangeTracker.Clear();
        var message = await query.FirstOrDefaultAsync(ct);
        if (message is null)
            return false;

        message.AiProcessingStatus = WhatsAppAiProcessingStatus.Completed;
        message.AiProcessedAt = processedAt;
        message.AiProcessingError = null;
        message.AiProcessingStartedAt = null;
        message.AiNextRetryAt = null;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
