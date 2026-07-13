using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SenorArroz.API.Services;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public class WhatsAppAiWorkQueueRecoveryTests
{
    [Fact]
    public async Task Duplicate_IsRejectedWhileQueuedAndInFlight_AndAllowedAfterCompletion()
    {
        var queue = new WhatsAppAiWorkQueue();

        Assert.True(queue.TryEnqueue(10, 20));
        Assert.False(queue.TryEnqueue(10, 20));

        await using var reader = queue.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(new WhatsAppAiWorkItem(10, 20), reader.Current);
        Assert.False(queue.TryEnqueue(10, 20));

        queue.MarkCompleted(reader.Current);

        Assert.True(queue.TryEnqueue(10, 20));
    }

    [Fact]
    public async Task FullQueue_DoesNotLeavePhantomReservation()
    {
        var queue = new WhatsAppAiWorkQueue();
        for (var messageId = 1; messageId <= 500; messageId++)
        {
            Assert.True(queue.TryEnqueue(1, messageId));
        }

        Assert.False(queue.TryEnqueue(2, 501));

        await using var reader = queue.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        queue.MarkCompleted(reader.Current);

        Assert.True(queue.TryEnqueue(2, 501));
    }

    [Fact]
    public async Task Recovery_HandlesEveryInterruptedStateWithoutUnsafeResendOrDuplicateEnqueue()
    {
        await using var provider = CreateServices();
        var stale = DateTime.UtcNow.AddMinutes(-10);

        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.WhatsAppMessages.AddRange(
                Inbound(1, WhatsAppAiProcessingStatus.Processing, stale),
                Inbound(2, WhatsAppAiProcessingStatus.ResponseGenerated, stale, attemptId: "response-2"),
                Inbound(3, WhatsAppAiProcessingStatus.Sending, stale, attemptId: "uncertain-3"),
                Inbound(4, WhatsAppAiProcessingStatus.Sent, stale, whatsAppMessageId: "wamid-4"),
                Inbound(5, WhatsAppAiProcessingStatus.Sending, stale, attemptId: "confirmed-5"),
                Inbound(6, WhatsAppAiProcessingStatus.Sent, stale, attemptId: "missing-6"),
                Inbound(7, WhatsAppAiProcessingStatus.Processing, DateTime.UtcNow),
                Inbound(8, WhatsAppAiProcessingStatus.Processing, stale, attempts: 3),
                Inbound(9, WhatsAppAiProcessingStatus.Pending, stale, attempts: 3));
            db.WhatsAppMessages.Add(new WhatsAppMessage
            {
                Id = 50,
                ConversationId = 5,
                Direction = WhatsAppMessageDirection.Outbound,
                Type = WhatsAppMessageType.Text,
                TextBody = "respuesta",
                Status = WhatsAppMessageStatus.Sent,
                Timestamp = stale,
                SentByAi = true,
                WhatsAppMessageId = "wamid-5",
                RawPayload = "{\"origin\":\"ai\",\"attemptId\":\"confirmed-5\",\"success\":true}"
            });
            await db.SaveChangesAsync();
        }

        var queue = new WhatsAppAiWorkQueue();
        var service = new TestRecoveryService(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new WhatsAppAiOrchestratorOptions
            {
                ProcessingStaleAfterSeconds = 120,
                RecoveryBatchSize = 100
            }));

        await service.RecoverAsync();
        // A second cycle must not append the same Pending work items again.
        await service.RecoverAsync();

        await using (var assertionScope = provider.CreateAsyncScope())
        {
            var db = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var messages = await db.WhatsAppMessages
                .Where(message => message.Direction == WhatsAppMessageDirection.Inbound)
                .ToDictionaryAsync(message => message.Id);

            Assert.True(
                messages.ContainsKey(1),
                $"Expected inbound ids 1-7, actual: {string.Join(',', messages.Keys.Order())}");

            Assert.Equal(WhatsAppAiProcessingStatus.Pending, messages[1].AiProcessingStatus);
            Assert.Equal(WhatsAppAiProcessingStatus.Pending, messages[2].AiProcessingStatus);

            Assert.Equal(WhatsAppAiProcessingStatus.Failed, messages[3].AiProcessingStatus);
            Assert.Contains("no se reintentó", messages[3].AiProcessingError, StringComparison.OrdinalIgnoreCase);

            Assert.Equal(WhatsAppAiProcessingStatus.Completed, messages[4].AiProcessingStatus);
            Assert.Equal(WhatsAppAiProcessingStatus.Completed, messages[5].AiProcessingStatus);
            Assert.Equal("wamid-5", messages[5].AiResponseWhatsAppMessageId);

            Assert.Equal(WhatsAppAiProcessingStatus.Failed, messages[6].AiProcessingStatus);
            Assert.Contains("sin identificador", messages[6].AiProcessingError, StringComparison.OrdinalIgnoreCase);

            Assert.Equal(WhatsAppAiProcessingStatus.Processing, messages[7].AiProcessingStatus);
            Assert.Equal(WhatsAppAiProcessingStatus.Failed, messages[8].AiProcessingStatus);
            Assert.Contains("máximo de intentos", messages[8].AiProcessingError, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(WhatsAppAiProcessingStatus.Failed, messages[9].AiProcessingStatus);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await using var reader = queue.ReadAllAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        Assert.True(await reader.MoveNextAsync());
        var first = reader.Current;
        Assert.True(await reader.MoveNextAsync());
        var second = reader.Current;
        Assert.Equal([1, 2], new[] { first.MessageId, second.MessageId });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.MoveNextAsync().AsTask());
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        var databaseName = $"whatsapp-ai-recovery-{Guid.NewGuid():N}";
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        return services.BuildServiceProvider();
    }

    private static WhatsAppMessage Inbound(
        int id,
        WhatsAppAiProcessingStatus status,
        DateTime startedAt,
        string? attemptId = null,
        string? whatsAppMessageId = null,
        int attempts = 0) => new()
        {
            Id = id,
            ConversationId = id,
            Direction = WhatsAppMessageDirection.Inbound,
            Type = WhatsAppMessageType.Text,
            TextBody = "Hola",
            Status = WhatsAppMessageStatus.Received,
            Timestamp = startedAt,
            AiProcessingStatus = status,
            AiProcessingStartedAt = startedAt,
            AiProcessingAttempts = attempts,
            AiResponseAttemptId = attemptId,
            AiResponseWhatsAppMessageId = whatsAppMessageId
        };

    private sealed class TestRecoveryService(
        WhatsAppAiWorkQueue queue,
        IServiceScopeFactory scopes,
        IOptions<WhatsAppAiOrchestratorOptions> options)
        : WhatsAppAiRecoveryService(
            queue,
            scopes,
            options,
            NullLogger<WhatsAppAiRecoveryService>.Instance)
    {
        public Task RecoverAsync() => RecoverOnceAsync(CancellationToken.None);
    }
}
