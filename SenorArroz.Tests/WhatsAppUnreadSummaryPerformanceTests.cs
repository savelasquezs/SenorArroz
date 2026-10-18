using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Testcontainers.PostgreSql;
using SenorArroz.API.Controllers;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Services;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public sealed class WhatsAppUnreadSummaryPerformanceTests
{
    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Unread_summary_executes_one_aggregate_query_with_PostgreSql()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        var counter = new SqlCounter();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).AddInterceptors(counter).Options;
        await using var db = new ApplicationDbContext(options,
            currentTenant: TestTenantContext.Default,
            tenantExecutionContext: TestTenantContext.Default);
        await db.Database.EnsureCreatedAsync();

        db.Tenants.Add(new Tenant { Id = 1, Name = "Perf", Slug = "perf" });
        db.Branches.Add(new Branch { Id = 1, TenantId = 1, Name = "One", Address = "-", Phone1 = "0" });
        db.WhatsAppChannelSettings.Add(new WhatsAppChannelSetting
        {
            Id = 1, TenantId = 1, IsActive = true, IsVerified = true,
            PhoneNumberId = "perf-channel"
        });
        var latest = new DateTime(2026, 10, 18, 12, 5, 0, DateTimeKind.Utc);
        db.WhatsAppConversations.AddRange(
            new WhatsAppConversation
            {
                Id = 11, TenantId = 1, BranchId = 1, ChannelSettingId = 1,
                OperationalBranchId = 1, UnreadCount = 2, LastMessageAt = latest.AddMinutes(-5)
            },
            new WhatsAppConversation
            {
                Id = 12, TenantId = 1, BranchId = 1, ChannelSettingId = 1,
                UnreadCount = 3, LastMessageAt = latest
            });
        await db.SaveChangesAsync();

        counter.Clear();
        var result = await Controller(db).GetUnreadSummary(default);
        var payload = Assert.IsType<OkObjectResult>(result.Result).Value as ApiResponse<WhatsAppUnreadSummaryDto>;
        Assert.NotNull(payload?.Data);
        Assert.Equal(5, payload.Data.TotalUnread);
        Assert.Equal(2, payload.Data.UnreadConversations);
        Assert.Equal(latest, payload.Data.LatestMessageAt);
        Assert.Equal(3, counter.ReaderCount);
        Assert.Single(counter.Queries.Where(x => x.Contains("whatsapp_conversation", StringComparison.OrdinalIgnoreCase)));

        foreach (var row in db.WhatsAppConversations.Local) row.UnreadCount = 0;
        await db.SaveChangesAsync();
        counter.Clear();
        var emptyResult = await Controller(db).GetUnreadSummary(default);
        var emptyPayload = Assert.IsType<OkObjectResult>(emptyResult.Result).Value as ApiResponse<WhatsAppUnreadSummaryDto>;
        Assert.NotNull(emptyPayload?.Data);
        Assert.Equal(0, emptyPayload.Data.TotalUnread);
        Assert.Equal(0, emptyPayload.Data.UnreadConversations);
        Assert.Null(emptyPayload.Data.LatestMessageAt);
        Assert.Equal(3, counter.ReaderCount);
    }

    private static WhatsAppController Controller(ApplicationDbContext db)
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.Id).Returns(7);
        user.SetupGet(x => x.Role).Returns("Cashier");
        user.SetupGet(x => x.BranchId).Returns(1);
        return new WhatsAppController(
            db, user.Object, Mock.Of<IBranchContext>(),
            Mock.Of<IClock>(), Mock.Of<IWhatsAppCloudClient>(),
            Mock.Of<IWhatsAppNotificationService>(),
            Mock.Of<IFirebaseGcsStorage>(),
            Options.Create(new FirebaseStorageOptions()),
            Options.Create(new WhatsAppCloudOptions()),
            Options.Create(new WhatsAppAiOrchestratorOptions()),
            NullLogger<WhatsAppController>.Instance,
            new WhatsAppAttentionService(), Mock.Of<IWhatsAppAiWorkQueue>(),
            Mock.Of<IWhatsAppAutomaticMessageSender>(),
            Mock.Of<IBranchBusinessHoursService>(),
            null!, null!, TestTenantContext.Default, TestTenantContext.Default);
    }

    private sealed class SqlCounter : DbCommandInterceptor
    {
        public List<string> Queries { get; } = new();
        public int ReaderCount => Queries.Count;
        public void Clear() => Queries.Clear();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
