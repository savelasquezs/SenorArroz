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
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Services;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public sealed class WhatsAppUnreadSummaryPerformanceTests
{
    private static readonly DateTime Latest = new(2026, 10, 18, 12, 5, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Cashier", 7, 1, 14, 4)]
    [InlineData("Admin", 7, 1, 14, 4)]
    [InlineData("Cashier", 99, 1, 10, 3)]
    [InlineData("Superadmin", 7, 1, 22, 5)]
    [InlineData("Superadmin", 7, 2, 24, 5)]
    public async Task Summary_and_list_share_channel_and_role_visibility(
        string role, int userId, int branchId, int total, int count)
    {
        await using var db = Db(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await SeedAsync(db);
        var controller = Controller(db, role, userId, branchId);
        var summary = await SummaryAsync(controller);
        Assert.Equal(total, summary.TotalUnread);
        Assert.Equal(count, summary.UnreadConversations);
        var result = await controller.GetConversations(new WhatsAppConversationSearchDto { UnreadOnly = true }, default);
        var rows = Assert.IsType<ApiResponse<IReadOnlyList<WhatsAppConversationDto>>>(
            Assert.IsType<OkObjectResult>(result.Result).Value).Data!;
        Assert.Equal(summary.TotalUnread, rows.Sum(x => x.UnreadCount));
        Assert.Equal(summary.UnreadConversations, rows.Count);
        Assert.Equal(summary.LatestMessageAt, rows.Max(x => x.LastMessageAt));
        Assert.DoesNotContain(rows, x => x.Id is 14 or 15 or 19 or 20);
        Assert.IsType<ForbidResult>((await controller.GetMessages(14, default)).Result);
        Assert.IsType<ForbidResult>((await controller.GetMessages(15, default)).Result);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Unread_summary_executes_one_scoped_aggregate_query_with_PostgreSql()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        var counter = new SqlCounter();
        await using var db = Db(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).AddInterceptors(counter).Options);
        await db.Database.EnsureCreatedAsync();
        await SeedAsync(db);
        var controller = Controller(db);

        counter.Queries.Clear();
        var summary = await SummaryAsync(controller);
        Assert.Equal(14, summary.TotalUnread);
        Assert.Equal(4, summary.UnreadConversations);
        Assert.Equal(Latest, summary.LatestMessageAt);
        var sql = Assert.Single(counter.Queries);
        Assert.Contains("sum(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("whatsapp_channel_setting", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("whatsapp_branch_setting", sql, StringComparison.OrdinalIgnoreCase);

        // Execute the shared predicate through the actual relational listing too.
        var listed = await controller.GetConversations(new WhatsAppConversationSearchDto { UnreadOnly = true }, default);
        var rows = Assert.IsType<ApiResponse<IReadOnlyList<WhatsAppConversationDto>>>(
            Assert.IsType<OkObjectResult>(listed.Result).Value).Data!;
        Assert.Equal(new[] { 10, 11, 12, 16 }, rows.Select(x => x.Id).OrderBy(x => x));

        using (TestTenantContext.Default.BeginTenantScope(2))
        {
            counter.Queries.Clear();
            var otherTenant = await SummaryAsync(Controller(db, branchId: 3));
            Assert.Equal(90, otherTenant.TotalUnread);
            Assert.Equal(1, otherTenant.UnreadConversations);
            Assert.Single(counter.Queries);
        }

        // Positive unread counts with exclusively null timestamps must remain valid.
        foreach (var row in db.WhatsAppConversations.Local.Where(x => x.TenantId == 1))
            row.LastMessageAt = null;
        await db.SaveChangesAsync();
        counter.Queries.Clear();
        summary = await SummaryAsync(controller);
        Assert.Equal(14, summary.TotalUnread);
        Assert.Null(summary.LatestMessageAt);
        Assert.Single(counter.Queries);

        // The legacy channel is independent of the central-channel switches.
        foreach (var setting in db.WhatsAppChannelSettings.Local.Where(x => x.TenantId == 1))
            setting.IsActive = false;
        await db.SaveChangesAsync();
        counter.Queries.Clear();
        summary = await SummaryAsync(controller);
        Assert.Equal(5, summary.TotalUnread);
        Assert.Equal(1, summary.UnreadConversations);
        Assert.Single(counter.Queries);

        db.WhatsAppBranchSettings.Local.Single().IsActive = false;
        await db.SaveChangesAsync();
        counter.Queries.Clear();
        summary = await SummaryAsync(controller);
        Assert.Equal(0, summary.TotalUnread);
        Assert.Equal(0, summary.UnreadConversations);
        Assert.Null(summary.LatestMessageAt);
        Assert.Single(counter.Queries);

        // Enabled channel, but no unread rows (as opposed to no configuration).
        db.WhatsAppChannelSettings.Local.Single(x => x.Id == 1).IsActive = true;
        foreach (var row in db.WhatsAppConversations.Local.Where(x => x.TenantId == 1))
            row.UnreadCount = 0;
        await db.SaveChangesAsync();
        counter.Queries.Clear();
        summary = await SummaryAsync(controller);
        Assert.Equal(0, summary.TotalUnread);
        Assert.Equal(0, summary.UnreadConversations);
        Assert.Null(summary.LatestMessageAt);
        Assert.Single(counter.Queries);
    }

    private static ApplicationDbContext Db(DbContextOptions<ApplicationDbContext> options) => new(options,
        currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);

    private static async Task SeedAsync(ApplicationDbContext db)
    {
        using var scope = TestTenantContext.Default.BeginSystemScope();
        db.Tenants.AddRange(new Tenant { Id = 1, Name = "One", Slug = "one" },
            new Tenant { Id = 2, Name = "Two", Slug = "two" });
        db.Branches.AddRange(Enumerable.Range(1, 3).Select(id => new Branch
        {
            Id = id, TenantId = id == 3 ? 2 : 1, Name = $"Branch {id}", Address = "-", Phone1 = "0"
        }));
        db.Users.AddRange(new User { Id = 7, TenantId = 1, BranchId = 1, Name = "A", Email = "a@test.local", PasswordHash = "test", Role = UserRole.Cashier },
            new User { Id = 8, TenantId = 1, BranchId = 2, Name = "B", Email = "b@test.local", PasswordHash = "test", Role = UserRole.Cashier });
        db.WhatsAppBranchSettings.Add(new WhatsAppBranchSetting
        {
            Id = 1, TenantId = 1, BranchId = 1, IsActive = true, IsVerified = true, PhoneNumberId = "legacy-test"
        });
        db.WhatsAppChannelSettings.AddRange(Enumerable.Range(1, 4).Select(id => new WhatsAppChannelSetting
        {
            Id = id, TenantId = id == 4 ? 2 : 1, IsActive = id != 2, IsVerified = id != 3,
            PhoneNumberId = $"test-channel-{id}"
        }));
        WhatsAppConversation Conversation(int id, int? channel, int? operational, int unread, int? assigned = null) => new()
        {
            Id = id, TenantId = 1, BranchId = 1, ChannelSettingId = channel, OperationalBranchId = operational,
            AssignedUserId = assigned, UnreadCount = unread, LastMessageAt = Latest.AddMinutes(id - 11)
        };
        db.WhatsAppConversations.AddRange(
            Conversation(10, 1, 1, 2), Conversation(11, 1, null, 3),
            Conversation(12, 1, 2, 4, 7), Conversation(13, 1, 2, 8, 8),
            Conversation(14, 2, 1, 50), Conversation(15, 3, 1, 60),
            Conversation(16, null, null, 5), Conversation(18, 1, 1, 0),
            Conversation(20, 4, 1, 100));
        db.WhatsAppConversations.Add(new WhatsAppConversation
        {
            Id = 17, TenantId = 1, BranchId = 2, UnreadCount = 7, LastMessageAt = Latest
        });
        db.WhatsAppConversations.Add(new WhatsAppConversation
        {
            Id = 19, TenantId = 2, BranchId = 3, ChannelSettingId = 4, UnreadCount = 90, LastMessageAt = Latest
        });
        // Make both visible timestamp-less rows exercise nullable MAX.
        db.WhatsAppConversations.Local.Single(x => x.Id == 12).LastMessageAt = null;
        db.WhatsAppConversations.Local.Single(x => x.Id == 16).LastMessageAt = null;
        await db.SaveChangesAsync();
    }

    private static async Task<WhatsAppUnreadSummaryDto> SummaryAsync(WhatsAppController controller) =>
        Assert.IsType<ApiResponse<WhatsAppUnreadSummaryDto>>(
            Assert.IsType<OkObjectResult>((await controller.GetUnreadSummary(default)).Result).Value).Data!;

    private static WhatsAppController Controller(ApplicationDbContext db, string role = "Cashier", int userId = 7, int branchId = 1)
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.Id).Returns(userId);
        user.SetupGet(x => x.Role).Returns(role);
        user.SetupGet(x => x.BranchId).Returns(branchId);
        var branch = new Mock<IBranchContext>();
        branch.Setup(x => x.RequireBranch(It.IsAny<int?>())).Returns(branchId);
        return new WhatsAppController(
            db, user.Object, branch.Object,
            Mock.Of<IClock>(), Mock.Of<IWhatsAppCloudClient>(),
            Mock.Of<IWhatsAppNotificationService>(), Mock.Of<IFirebaseGcsStorage>(),
            Options.Create(new FirebaseStorageOptions()), Options.Create(new WhatsAppCloudOptions()),
            Options.Create(new WhatsAppAiOrchestratorOptions()), NullLogger<WhatsAppController>.Instance,
            new WhatsAppAttentionService(), Mock.Of<IWhatsAppAiWorkQueue>(),
            Mock.Of<IWhatsAppAutomaticMessageSender>(), Mock.Of<IBranchBusinessHoursService>(),
            null!, null!, TestTenantContext.Default, TestTenantContext.Default);
    }

    private sealed class SqlCounter : DbCommandInterceptor
    {
        public List<string> Queries { get; } = new();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
