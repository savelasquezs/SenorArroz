using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Services;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed partial class WhatsAppProcessingPostgreSqlTests
{
    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Stage23_Manual_reply_handles_legacy_timestamp_and_preserves_newer_customer_pending()
    {
        using var tenant = _tenant.BeginTenantScope(1);
        await using var db = Db();
        await SeedManualAsync(db);
        var conversation = await db.WhatsAppConversations.SingleAsync();
        conversation.NeedsHumanResponse = true;
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE whatsapp_message ALTER COLUMN timestamp TYPE timestamp without time zone USING timestamp AT TIME ZONE 'UTC'");
        var cloud = new Mock<IWhatsAppCloudClient>();
        cloud.Setup(x => x.SendTextMessageAsync("central", "local-token", "573000000001", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new WhatsAppCloudSendResult(true, "wamid.reply." + Guid.NewGuid().ToString("N"), null));
        var controller = Stage23Controller(db, 7, cloud: cloud.Object);
        Assert.IsType<OkObjectResult>((await controller.SendMessage(11, new() { Text = "Recoge en Manrique" }, default)).Result);
        Assert.Equal(DateTimeKind.Unspecified, (await db.WhatsAppMessages.AsNoTracking().SingleAsync()).Timestamp.Kind);
        await db.Entry(conversation).ReloadAsync();
        Assert.False(conversation.NeedsHumanResponse);
        conversation.LastCustomerMessageAt = _clock.UtcNow.AddSeconds(1);
        conversation.NeedsHumanResponse = true;
        await db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>((await controller.SendMessage(11, new() { Text = "Resumen anterior" }, default)).Result);
        await db.Entry(conversation).ReloadAsync();
        Assert.True(conversation.NeedsHumanResponse);
        Assert.Equal(2, await db.WhatsAppMessages.CountAsync());
        var order = await db.Orders.SingleAsync();
        Assert.Equal(1, order.OriginBranchId);
        Assert.Equal(2, order.BranchId);
        cloud.Verify(x => x.SendTextMessageAsync("central", "local-token", "573000000001", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Stage23_Conversation_template_is_durable_and_preserves_the_sender_after_window_expiry()
    {
        using var tenant = _tenant.BeginTenantScope(1);
        await using var db = Db(); await SeedManualAsync(db);
        (await db.WhatsAppChannelSettings.SingleAsync()).BusinessAccountId = "central-account";
        (await db.WhatsAppConversations.SingleAsync()).LastCustomerMessageAt = _clock.UtcNow.AddDays(-3);
        await db.SaveChangesAsync();
        var (queue, cloud, _, http) = ManualQueue(db);
        cloud.Setup(x => x.GetMessageTemplatesAsync("central-account", "local-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppCloudTemplateSyncResult(true,
                [new("id", "retomar", "es", "UTILITY", "APPROVED", "[{\"type\":\"BODY\",\"text\":\"Hola {{1}}\"}]")], null));
        cloud.Setup(x => x.SendTemplateMessageAsync("central", "local-token", "573000000001", "retomar", "es", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppCloudSendResult(true, "wamid.stage23-template", null));
        Assert.IsType<ObjectResult>((await queue.EnqueueAsync<WhatsAppMessageDto>("send-conversation-template", 11,
            JsonSerializer.SerializeToElement(new { templateName = "retomar", language = "es", parameters = new[] { "Cliente" } }), http.HttpContext!.Request, default)).Result);
        var job = (await Store(db).ClaimAsync(default))!;
        await ExecuteManualWorkerAsync(queue, job, db);
        Assert.True(await Store(db).FinishAsync(job, true, default));
        Assert.Equal(200, Assert.IsType<ObjectResult>(await queue.GetResultAsync(job.Id, default)).StatusCode);
        var message = await db.WhatsAppMessages.AsNoTracking().SingleAsync();
        Assert.Equal(11, message.ConversationId); Assert.Equal("Hola Cliente", message.TextBody); Assert.Equal(7, message.SentByUserId);
        Assert.Equal(1, await db.Orders.CountAsync());
        cloud.Verify(x => x.SendTemplateMessageAsync("central", "local-token", "573000000001", "retomar", "es", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Stage23_Simultaneous_advisors_take_one_owner_and_one_audit_without_changing_remote_order()
    {
        using var tenant = _tenant.BeginTenantScope(1);
        await using (var seed = Db())
        {
            await SeedManualAsync(seed);
            seed.Users.Add(new User { Id = 8, BranchId = 1, Role = UserRole.Cashier, Name = "Segundo", Email = "second@example.invalid" });
            var conversation = await seed.WhatsAppConversations.SingleAsync();
            conversation.AssignedUserId = null; conversation.AttentionMode = WhatsAppAttentionMode.WaitingForHuman;
            await seed.SaveChangesAsync();
        }
        await using var firstDb = Db(); await using var secondDb = Db();
        var responses = await Task.WhenAll(Stage23Controller(firstDb, 7).ExecuteAttentionAction(11,
            new() { Action = "take", ExpectedVersion = 0 }, default), Stage23Controller(secondDb, 8).ExecuteAttentionAction(11,
            new() { Action = "take", ExpectedVersion = 0 }, default));
        Assert.Single(responses, x => x.Result is OkObjectResult);
        Assert.Single(responses, x => x.Result is ConflictObjectResult);
        await using var verify = Db();
        var current = await verify.WhatsAppConversations.SingleAsync();
        Assert.Contains(current.AssignedUserId, new int?[] { 7, 8 }); Assert.Equal(1, current.AttentionRevision);
        Assert.Equal(1, await verify.WhatsAppWebhookEvents.CountAsync(x => x.EventType == "attention_action"));
        var order = await verify.Orders.SingleAsync(); Assert.Equal(2, order.BranchId); Assert.Equal(1, order.OriginBranchId);
        using (_tenant.BeginTenantScope(2)) Assert.Empty(await verify.WhatsAppConversations.ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Stage23_Schema_is_additive_idempotent_and_conversation_versions_reject_stale_writers()
    {
        using var tenant = _tenant.BeginTenantScope(1);
        await using var db = Db(); await SeedManualAsync(db);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE whatsapp_conversation DROP COLUMN last_customer_message_at, DROP COLUMN needs_human_response, DROP COLUMN attention_revision;
            ALTER TABLE whatsapp_message DROP COLUMN source, DROP COLUMN is_historical;
            ALTER TABLE whatsapp_branch_setting DROP COLUMN coexistence_enabled, DROP COLUMN connection_state,
                DROP COLUMN connection_error, DROP COLUMN coexistence_connected_at, DROP COLUMN history_sync_status,
                DROP COLUMN contacts_sync_status, DROP COLUMN history_sync_request_id, DROP COLUMN contacts_sync_request_id, DROP COLUMN last_sync_at;
            """);
        var script = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "SenorArroz.Infrastructure", "Scripts", "add_whatsapp_coexistence_attention.sql"));
        await db.Database.ExecuteSqlRawAsync(script); await db.Database.ExecuteSqlRawAsync(script);
        await using var first = Db(); await using var second = Db();
        var a = await first.WhatsAppConversations.SingleAsync(); var b = await second.WhatsAppConversations.SingleAsync();
        new WhatsAppAttentionService().Release(a, 7, _clock.UtcNow); await first.SaveChangesAsync();
        b.CustomerId = null; b.ContactName = "Stale";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        Assert.Equal(1, await db.WhatsAppConversations.AsNoTracking().Select(x => x.AttentionRevision).SingleAsync());
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Stage23_Guided_signup_verifies_assets_subscribes_once_and_rejects_session_replay()
    {
        using var tenant = _tenant.BeginTenantScope(1);
        await using var db = Db(); await SeedManualAsync(db);
        var calls = 0;
        var meta = WhatsAppCoexistenceTests.MetaClient(request =>
        {
            calls++;
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/subscriptions")) return JsonSerializer.Serialize(new { data = new[] { new { @object = "whatsapp_business_account",
                fields = new[] { "messages", "history", "smb_app_state_sync", "smb_message_echoes", "account_update" }.Select(name => new { name }) } } });
            if (path.EndsWith("/oauth/access_token")) return "{\"access_token\":\"local-test-token\"}";
            if (path.EndsWith("/debug_token")) return JsonSerializer.Serialize(new { data = new { is_valid = true, app_id = "10",
                scopes = new[] { "whatsapp_business_management", "whatsapp_business_messaging" },
                granular_scopes = new[] { "whatsapp_business_management", "whatsapp_business_messaging" }.Select(scope => new { scope, target_ids = new[] { "123" } }) } });
            if (path.EndsWith("/phone_numbers")) return "{\"data\":[{\"id\":\"456\",\"display_phone_number\":\"573111111111\",\"is_on_biz_app\":true,\"platform_type\":\"CLOUD_API\"}]}";
            if (path.EndsWith("/subscribed_apps")) return request.Method == HttpMethod.Get
                ? """{"data":[]}""" : """{"success":true}""";
            if (path.EndsWith("/smb_app_data"))
            {
                using var callback = Db();
                var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                if (body.Contains("\"history\"")) callback.WhatsAppBranchSettings.Where(x => x.BranchId == 1)
                    .ExecuteUpdate(x => x.SetProperty(s => s.HistorySyncStatus, "completed").SetProperty(s => s.ConnectionState, "connected"));
                else callback.WhatsAppBranchSettings.Where(x => x.BranchId == 1)
                    .ExecuteUpdate(x => x.SetProperty(s => s.ContactsSyncStatus, "received"));
            }
            return "{\"request_id\":\"local-request\"}";
        });
        var actor = Mock.Of<ICurrentUser>(x => x.Id == 7 && x.Role == "Admin" && x.BranchId == 1 && x.IsAuthenticated);
        var controller = new WhatsAppCoexistenceController(db, _tenant, actor, _clock, _protector, meta);
        var start = Assert.IsType<SenorArroz.Shared.Models.ApiResponse<object>>(Assert.IsType<OkObjectResult>(await controller.Start(1, default)).Value);
        var session = JsonSerializer.SerializeToElement(start.Data).GetProperty("session").GetString()!;
        var request = new WhatsAppCoexistenceController.ConnectRequest(session, "temporary-local-code", "123", "456");
        var result = await controller.Connect(1, request, default);
        Assert.IsType<OkObjectResult>(result); Assert.Equal(8, calls);
        var line = await db.WhatsAppBranchSettings.AsNoTracking().SingleAsync();
        Assert.True(line.CoexistenceEnabled && line.IsVerified); Assert.Equal("connected", line.ConnectionState);
        Assert.Equal("completed", line.HistorySyncStatus); Assert.Equal("received", line.ContactsSyncStatus);
        Assert.DoesNotContain("local-test-token", JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(result).Value));
        Assert.IsType<ConflictObjectResult>(await controller.Connect(1, request, default)); Assert.Equal(8, calls);
    }

    private WhatsAppController Stage23Controller(ApplicationDbContext db, int actor, int branchId = 1, string role = "Cashier", IBranchContext? branchContext = null, IWhatsAppCloudClient? cloud = null) => new(db,
        Mock.Of<ICurrentUser>(x => x.Id == actor && x.Role == role && x.BranchId == branchId && x.IsAuthenticated),
        branchContext ?? Mock.Of<IBranchContext>(x => x.EffectiveBranchId == branchId), _clock, cloud ?? Mock.Of<IWhatsAppCloudClient>(),
        Mock.Of<IWhatsAppNotificationService>(), Mock.Of<IFirebaseGcsStorage>(), Options.Create(new FirebaseStorageOptions()),
        Options.Create(new WhatsAppCloudOptions()), Options.Create(new WhatsAppAiOrchestratorOptions()),
        NullLogger<WhatsAppController>.Instance, new WhatsAppAttentionService(), Mock.Of<IWhatsAppAiWorkQueue>(),
        Mock.Of<IWhatsAppAutomaticMessageSender>(), Mock.Of<IBranchBusinessHoursService>(), null!, null!, _tenant, _tenant);
}
