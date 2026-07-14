using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Services;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public class HumanAssistanceAgentToolTests
{
    [Fact]
    public async Task RequestHuman_ChangesModeNotifiesAndUsesConfiguredMessage()
    {
        await using var db = await CreateDb();
        var notifications = new Mock<IWhatsAppNotificationService>();
        var sender = new Mock<IWhatsAppAutomaticMessageSender>();
        sender.Setup(x => x.SendTransferTextAsync(
                1,
                10,
                It.IsAny<string>(),
                "Ya te comunicamos con un asesor.",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppAutomaticSendResult(true, false, "wamid", null));

        var result = await Execute(db, notifications, sender);

        Assert.True(result.TransferredToHuman);
        Assert.Equal(WhatsAppAttentionMode.WaitingForHuman, (await db.WhatsAppConversations.FindAsync(1))!.AttentionMode);
        notifications.Verify(x => x.NotifyAttentionChangedAsync(
            1,
            It.Is<WhatsAppConversationDto>(conversation =>
                conversation.AttentionReason == "El cliente solicita asesor"),
            It.IsAny<CancellationToken>()), Times.Once);
        sender.VerifyAll();
    }

    [Fact]
    public async Task RealtimeNotificationFailure_DoesNotPreventCustomerNotice()
    {
        await using var db = await CreateDb();
        var notifications = new Mock<IWhatsAppNotificationService>();
        notifications.Setup(x => x.NotifyAttentionChangedAsync(
                It.IsAny<int>(),
                It.IsAny<WhatsAppConversationDto>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SignalR unavailable"));
        var sender = new Mock<IWhatsAppAutomaticMessageSender>();
        sender.Setup(x => x.SendTransferTextAsync(
                1,
                10,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppAutomaticSendResult(true, false, "wamid", null));

        var result = await Execute(db, notifications, sender);

        Assert.True(result.TransferredToHuman);
        sender.Verify(x => x.SendTransferTextAsync(
            1,
            10,
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TransferDeliveryFailure_IsPersistedAndReturnedAsWarning()
    {
        await using var db = await CreateDb();
        var notifications = new Mock<IWhatsAppNotificationService>();
        var sender = new Mock<IWhatsAppAutomaticMessageSender>();
        sender.Setup(x => x.SendTransferTextAsync(
                1,
                10,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppAutomaticSendResult(
                false,
                false,
                null,
                "Meta WhatsApp HTTP 400: destinatario inválido"));

        var result = await Execute(db, notifications, sender);

        var incoming = await db.WhatsAppMessages.FindAsync(10);
        Assert.True(result.TransferredToHuman);
        Assert.Contains(result.Warnings!, x => x.Contains("HTTP 400"));
        Assert.Equal(WhatsAppAiProcessingStatus.TransferredToHuman, incoming!.AiProcessingStatus);
        Assert.Contains("Aviso al cliente no entregado", incoming.AiProcessingError);
        Assert.Contains("HTTP 400", incoming.AiProcessingError);
        Assert.Equal(1, CountOccurrences(incoming.AiProcessingError!, "Aviso al cliente no entregado"));
    }

    private static async Task<AgentToolExecutionResult> Execute(
        ApplicationDbContext db,
        Mock<IWhatsAppNotificationService> notifications,
        Mock<IWhatsAppAutomaticMessageSender> sender)
    {
        var now = DateTime.UtcNow;
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(now);
        using var arguments = JsonDocument.Parse("""{"reason":"El cliente solicita asesor"}""");
        return await new RequestHumanAssistanceAgentTool(
                db,
                new WhatsAppAttentionService(),
                notifications.Object,
                sender.Object,
                clock.Object)
            .ExecuteAsync(new(1, 1, 10, ExecutionId: "run"), arguments.RootElement, default);
    }

    private static async Task<ApplicationDbContext> CreateDb()
    {
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        db.WhatsAppConversations.Add(new WhatsAppConversation
        {
            Id = 1,
            BranchId = 1,
            PhoneNumber = "300",
            AttentionMode = WhatsAppAttentionMode.Ai
        });
        db.WhatsAppMessages.Add(new WhatsAppMessage
        {
            Id = 10,
            ConversationId = 1,
            Direction = WhatsAppMessageDirection.Inbound,
            Type = WhatsAppMessageType.Text,
            TextBody = "asesor",
            Status = WhatsAppMessageStatus.Received,
            Timestamp = DateTime.UtcNow
        });
        db.BranchAiSettings.Add(new BranchAiSetting
        {
            BranchId = 1,
            TransferMessage = "Ya te comunicamos con un asesor."
        });
        await db.SaveChangesAsync();
        return db;
    }

    private static int CountOccurrences(string value, string term) =>
        (value.Length - value.Replace(term, string.Empty, StringComparison.Ordinal).Length) / term.Length;
}
