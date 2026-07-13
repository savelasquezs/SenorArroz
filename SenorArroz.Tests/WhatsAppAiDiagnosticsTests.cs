using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public class WhatsAppAiDiagnosticsTests
{
    [Fact]
    public void Mapper_ClassifiesQuotaPreservesHttpStatusAndRedactsSecrets()
    {
        var now = new DateTime(2026, 7, 13, 18, 0, 0, DateTimeKind.Utc);
        var receivedAt = now.AddMinutes(-1);
        var nextRetryAt = now.AddSeconds(10);
        var message = new WhatsAppMessage
        {
            Id = 7,
            ConversationId = 3,
            Direction = WhatsAppMessageDirection.Inbound,
            Type = WhatsAppMessageType.Text,
            Status = WhatsAppMessageStatus.Received,
            Timestamp = receivedAt,
            AiProcessingStatus = WhatsAppAiProcessingStatus.Pending,
            AiProcessingAttempts = 1,
            AiNextRetryAt = nextRetryAt,
            AiProcessedAt = now,
            AiProcessingError = "HTTP 429: RESOURCE_EXHAUSTED quota. Bearer very-secret-token https://example.test?key=also-secret Incorrect API key provided: sk-proj-another-secret"
        };

        var result = WhatsAppAiDiagnosticsMapper.ToDto(message, 3, observedAt: now);

        Assert.Equal("quota", result.ErrorCategory);
        Assert.Equal(429, result.HttpStatusCode);
        Assert.True(result.WillRetry);
        Assert.Equal(now, result.StatusChangedAt);
        Assert.DoesNotContain("very-secret-token", result.TechnicalDetail);
        Assert.DoesNotContain("also-secret", result.TechnicalDetail);
        Assert.DoesNotContain("sk-proj-another-secret", result.TechnicalDetail);
        Assert.Contains("[OCULTO]", result.TechnicalDetail);

        var dueResult = WhatsAppAiDiagnosticsMapper.ToDto(
            message,
            3,
            observedAt: nextRetryAt.AddSeconds(1));
        Assert.Equal(now, dueResult.StatusChangedAt);
    }

    [Fact]
    public void Mapper_DoesNotMisclassifyUnsupportedMessageAsUnsupportedModel()
    {
        var message = new WhatsAppMessage
        {
            Id = 8,
            ConversationId = 3,
            Direction = WhatsAppMessageDirection.Inbound,
            Type = WhatsAppMessageType.Audio,
            Status = WhatsAppMessageStatus.Received,
            Timestamp = DateTime.UtcNow,
            AiProcessingStatus = WhatsAppAiProcessingStatus.Ignored,
            AiProcessingError = "Tipo no soportado."
        };

        var result = WhatsAppAiDiagnosticsMapper.ToDto(message, 3);

        Assert.Equal("unsupported_message", result.ErrorCategory);
        Assert.DoesNotContain("modelo", result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tipo de mensaje", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mapper_KeepsTransferProviderBodyOutOfFriendlyDetail()
    {
        var message = new WhatsAppMessage
        {
            Id = 9,
            ConversationId = 3,
            Direction = WhatsAppMessageDirection.Inbound,
            Type = WhatsAppMessageType.Text,
            Status = WhatsAppMessageStatus.Received,
            Timestamp = DateTime.UtcNow,
            AiProcessingStatus = WhatsAppAiProcessingStatus.TransferredToHuman,
            AiProcessingError = "El cliente pidió asesor | Aviso al cliente no entregado: Meta WhatsApp HTTP 400 | body: {\"internal\":\"sensitive\"}"
        };

        var result = WhatsAppAiDiagnosticsMapper.ToDto(message, 3);

        Assert.Equal("meta", result.ErrorCategory);
        Assert.Contains("no se pudo entregar", result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitive", result.Detail);
        Assert.Contains("sensitive", result.TechnicalDetail);
    }

    [Fact]
    public void Mapper_ClassifiesUnsupportedProviderAsConfiguration()
    {
        var message = new WhatsAppMessage
        {
            Id = 10,
            ConversationId = 3,
            Direction = WhatsAppMessageDirection.Inbound,
            Type = WhatsAppMessageType.Text,
            Status = WhatsAppMessageStatus.Received,
            Timestamp = DateTime.UtcNow,
            AiProcessingStatus = WhatsAppAiProcessingStatus.Failed,
            AiProcessingError = "Proveedor no soportado."
        };

        var result = WhatsAppAiDiagnosticsMapper.ToDto(message, 3);

        Assert.Equal("configuration", result.ErrorCategory);
        Assert.DoesNotContain("modelo", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Endpoint_UsesLifecycleTimeForOrderingFailureWindowAndOverallStatus()
    {
        var now = new DateTime(2026, 7, 13, 18, 0, 0, DateTimeKind.Utc);
        await using var db = CreateDb();
        db.Branches.Add(new Branch { Id = 1, Name = "Centro" });
        db.BranchAiSettings.Add(new BranchAiSetting
        {
            Id = 1,
            BranchId = 1,
            Provider = "openai",
            Model = "gpt-4o-mini",
            ApiKey = "secret",
            IsActive = true,
            IsVerified = true
        });
        db.WhatsAppConversations.Add(new WhatsAppConversation
        {
            Id = 4,
            BranchId = 1,
            PhoneNumber = "573001112233",
            AttentionMode = WhatsAppAttentionMode.Ai
        });
        db.WhatsAppMessages.AddRange(
            new WhatsAppMessage
            {
                Id = 10,
                ConversationId = 4,
                Direction = WhatsAppMessageDirection.Inbound,
                Type = WhatsAppMessageType.Text,
                Status = WhatsAppMessageStatus.Received,
                Timestamp = now.AddHours(-2),
                AiProcessingStatus = WhatsAppAiProcessingStatus.Completed,
                AiProcessedAt = now,
                AiProcessingAttempts = 1
            },
            new WhatsAppMessage
            {
                Id = 11,
                ConversationId = 4,
                Direction = WhatsAppMessageDirection.Inbound,
                Type = WhatsAppMessageType.Text,
                Status = WhatsAppMessageStatus.Received,
                Timestamp = now.AddDays(-2),
                AiProcessingStatus = WhatsAppAiProcessingStatus.Failed,
                AiProcessedAt = now.AddMinutes(-1),
                AiProcessingAttempts = 3,
                AiProcessingError = "HTTP 429: quota exhausted"
            },
            new WhatsAppMessage
            {
                Id = 12,
                ConversationId = 4,
                Direction = WhatsAppMessageDirection.Inbound,
                Type = WhatsAppMessageType.Text,
                Status = WhatsAppMessageStatus.Received,
                Timestamp = now.AddHours(-26),
                AiProcessingStatus = WhatsAppAiProcessingStatus.Failed,
                AiProcessedAt = now.AddHours(-25),
                AiProcessingAttempts = 3,
                AiProcessingError = "HTTP 500: old failure"
            });
        await db.SaveChangesAsync();

        var action = await CreateController(db, "admin", 1, now).Get(1, 4, 20);
        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<ApiResponse<WhatsAppAiDiagnosticsDto>>(ok.Value).Data!;

        Assert.Equal(1, response.FailedCountLast24Hours);
        Assert.Equal("error", response.OverallStatus);
        Assert.Equal(now, response.LastActivityAt);
        Assert.Equal(10, response.RecentMessages[0].MessageId);
        Assert.Equal(now, response.RecentMessages[0].StatusChangedAt);
        Assert.Equal(11, response.RecentMessages[1].MessageId);
    }

    [Fact]
    public async Task Endpoint_ReportsDisabledAgentEvenWithoutRecentFailure()
    {
        await using var db = CreateDb();
        db.Branches.Add(new Branch { Id = 1, Name = "Centro" });
        db.BranchAiSettings.Add(new BranchAiSetting
        {
            Id = 1,
            BranchId = 1,
            Provider = "gemini",
            Model = "gemini-flash-latest",
            ApiKey = "secret",
            IsActive = false,
            IsVerified = true
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db, "admin", 1);
        var action = await controller.Get(1, null, 20);
        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<ApiResponse<WhatsAppAiDiagnosticsDto>>(ok.Value);

        Assert.Equal("disabled", response.Data!.AgentStatus);
        Assert.Equal("error", response.Data.OverallStatus);
        Assert.Contains("deshabilitado", response.Data.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("gemini-flash-latest", response.Data.Model);
    }

    [Fact]
    public async Task Endpoint_HidesTechnicalProviderBodyFromCashier()
    {
        await using var db = CreateDb();
        db.Branches.Add(new Branch { Id = 1, Name = "Centro" });
        db.BranchAiSettings.Add(new BranchAiSetting
        {
            Id = 1,
            BranchId = 1,
            Provider = "openai",
            Model = "gpt-4o-mini",
            ApiKey = "secret",
            IsActive = true,
            IsVerified = true
        });
        db.WhatsAppConversations.Add(new WhatsAppConversation
        {
            Id = 4,
            BranchId = 1,
            PhoneNumber = "573001112233",
            AttentionMode = WhatsAppAttentionMode.Ai
        });
        db.WhatsAppMessages.Add(new WhatsAppMessage
        {
            Id = 9,
            ConversationId = 4,
            Direction = WhatsAppMessageDirection.Inbound,
            Type = WhatsAppMessageType.Text,
            TextBody = "Hola",
            Status = WhatsAppMessageStatus.Received,
            Timestamp = DateTime.UtcNow,
            AiProcessingStatus = WhatsAppAiProcessingStatus.Failed,
            AiProcessingAttempts = 3,
            AiProcessingError = "HTTP 401: Invalid API key provider detail"
        });
        await db.SaveChangesAsync();

        var cashierAction = await CreateController(db, "cashier", 1).Get(1, 4, 20);
        var cashierOk = Assert.IsType<OkObjectResult>(cashierAction.Result);
        var cashier = Assert.IsType<ApiResponse<WhatsAppAiDiagnosticsDto>>(cashierOk.Value);
        Assert.Null(cashier.Data!.RecentMessages.Single().TechnicalDetail);
        Assert.Equal("authentication", cashier.Data.RecentMessages.Single().ErrorCategory);

        var adminAction = await CreateController(db, "admin", 1).Get(1, 4, 20);
        var adminOk = Assert.IsType<OkObjectResult>(adminAction.Result);
        var admin = Assert.IsType<ApiResponse<WhatsAppAiDiagnosticsDto>>(adminOk.Value);
        Assert.Contains("Invalid API key", admin.Data!.RecentMessages.Single().TechnicalDetail);
    }

    private static WhatsAppAiDiagnosticsController CreateController(
        ApplicationDbContext db,
        string role,
        int branchId,
        DateTime? now = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.Role).Returns(role);
        currentUser.SetupGet(x => x.BranchId).Returns(branchId);
        return new WhatsAppAiDiagnosticsController(
            db,
            currentUser.Object,
            new FakeClock(now ?? DateTime.UtcNow),
            Options.Create(new WhatsAppAiOrchestratorOptions { MaxPersistentAttempts = 3 }));
    }

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
