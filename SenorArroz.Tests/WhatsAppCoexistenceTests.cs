using System.Text.Json;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.API.Hosting;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Services;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Integrations;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public sealed class WhatsAppCoexistenceTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 18, 0, 0, DateTimeKind.Utc);
    private static string Payload(string field, object data, string line = "line-1") => JsonSerializer.Serialize(new
    {
        @object = "whatsapp_business_account", entry = new[] { new { id = "waba", changes = new[] {
            new { field, value = new Dictionary<string, object> { ["metadata"] = new { phone_number_id = line },
                [field == "smb_message_echoes" ? "message_echoes" : field == "history" ? "history" : "state_sync"] = data } } } } }
    });
    private static object Echo(string id = "echo-1") => new { from = "573111111111", to = "573000000001", id,
        timestamp = "1793901600", type = "text", text = new { body = "Te atiendo desde el celular" } };
    private static object EchoRecipient(string id, string userId, string? phone = null)
    {
        var echo = new Dictionary<string, object> { ["id"] = id, ["to_user_id"] = userId, ["type"] = "text",
            ["text"] = new { body = "Respuesta desde el celular" } };
        if (phone is not null) echo["to"] = phone;
        return echo;
    }
    private static ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, currentTenant: TestTenantContext.Default,
        tenantExecutionContext: TestTenantContext.Default);
    private static async Task Seed(ApplicationDbContext db)
    {
        db.AddRange(new Branch { Id = 1, Name = "Santander" },
            new Branch { Id = 2, Name = "Otra" },
            new WhatsAppBranchSetting { Id = 1, BranchId = 1, PhoneNumberId = "line-1", CoexistenceEnabled = true, IsActive = true, IsVerified = true },
            new WhatsAppBranchSetting { Id = 2, BranchId = 2, PhoneNumberId = "line-2", CoexistenceEnabled = true, IsActive = true, IsVerified = true });
        await db.SaveChangesAsync();
    }
    private static WhatsAppWebhookProcessor Processor(ApplicationDbContext db, Mock<IWhatsAppAiWorkQueue> queue,
        Mock<IWhatsAppNotificationService> notifications) => new(db, Mock.Of<IClock>(x => x.UtcNow == Now),
        Mock.Of<IWhatsAppCloudClient>(), notifications.Object, Mock.Of<IFirebaseGcsStorage>(), Options.Create(new FirebaseStorageOptions()),
        Options.Create(new WhatsAppAiOrchestratorOptions()), NullLogger<WhatsAppWebhookProcessor>.Instance,
        new WhatsAppAttentionService(), queue.Object, Mock.Of<IWhatsAppAutomaticMessageSender>(),
        Mock.Of<IBranchBusinessHoursService>(), null!, null!, TestTenantContext.Default);

    [Fact]
    public void Echo_and_history_use_the_contact_partition_and_keep_lines_separate()
    {
        var echo = Assert.Single(WhatsAppWebhookEnvelope.Split(Payload("smb_message_echoes", new[] { Echo() })));
        var inbound = Assert.Single(WhatsAppWebhookEnvelope.Split(WhatsAppProcessingTests.Payload().Replace("channel-1", "line-1")));
        var other = Assert.Single(WhatsAppWebhookEnvelope.Split(Payload("smb_message_echoes", new[] { Echo() }, "line-2")));
        Assert.Equal(inbound.Partition, echo.Partition);
        Assert.NotEqual(echo.Partition, other.Partition);
        Assert.Equal(echo.Key, Assert.Single(WhatsAppWebhookEnvelope.Split(Payload("smb_message_echoes", new[] { Echo() }))).Key);
    }

    [Fact]
    public void Echo_without_a_phone_adds_the_bsuid_alias_for_its_line()
    {
        var onlyBsuid = Assert.Single(WhatsAppWebhookEnvelope.Split(Payload("smb_message_echoes", new[] { EchoRecipient("one", "US.123") })));
        var both = Assert.Single(WhatsAppWebhookEnvelope.Split(Payload("smb_message_echoes", new[] { EchoRecipient("two", "US.123", "573000000001") })));
        var otherLine = Assert.Single(WhatsAppWebhookEnvelope.Split(Payload("smb_message_echoes", new[] { EchoRecipient("two", "US.123") }, "line-2")));
        Assert.Null(onlyBsuid.PhoneNumber);
        Assert.Equal("US.123", onlyBsuid.UserId);
        Assert.Equal(onlyBsuid.Partition, Assert.Single(onlyBsuid.Aliases));
        Assert.Contains(onlyBsuid.Partition, both.Aliases);
        Assert.NotEqual(onlyBsuid.Partition, otherLine.Partition);
    }

    [Fact]
    public async Task Echo_creates_or_reuses_the_branch_bsuid_without_rebinding_a_conflicting_number()
    {
        await using var db = Db();
        await Seed(db);
        var central = new WhatsAppChannelSetting { Id = 42, PhoneNumberId = "central" };
        db.AddRange(central, new WhatsAppConversation { BranchId = 1, ChannelSettingId = central.Id,
                WhatsAppUserId = "US.123", PhoneNumber = "573000000001" },
            new WhatsAppConversation { BranchId = 1, BranchLineId = 1, WhatsAppUserId = "US.previous", PhoneNumber = "573000000001" });
        await db.SaveChangesAsync();
        var processor = Processor(db, new(), new());

        await processor.ProcessAsync(new WhatsAppWebhookEvent(), Payload("smb_message_echoes",
            new[] { EchoRecipient("one", "US.123", "573000000001") }), default);
        var echo = Payload("smb_message_echoes", new[] { EchoRecipient("two", "US.123") });
        await processor.ProcessAsync(new WhatsAppWebhookEvent(), echo, default);
        await processor.ProcessAsync(new WhatsAppWebhookEvent(), echo, default);
        await processor.ProcessAsync(new WhatsAppWebhookEvent(), Payload("smb_message_echoes",
            new[] { EchoRecipient("three", "US.new") }), default);

        var thread = await db.WhatsAppConversations.SingleAsync(x => x.ChannelSettingId == null && x.WhatsAppUserId == "US.123");
        Assert.Null(thread.PhoneNumber);
        Assert.Equal(1, thread.BranchId);
        Assert.Equal(2, await db.WhatsAppMessages.CountAsync(x => x.ConversationId == thread.Id && x.Source == "businessApp"));
        Assert.Equal("573000000001", await db.WhatsAppConversations.Where(x => x.WhatsAppUserId == "US.previous").Select(x => x.PhoneNumber).SingleAsync());
        Assert.Null(await db.WhatsAppConversations.Where(x => x.WhatsAppUserId == "US.new").Select(x => x.PhoneNumber).SingleAsync());
        Assert.Equal(3, await db.WhatsAppMessages.CountAsync());
    }

    [Fact]
    public async Task Pilot_accepts_echo_without_phone_only_for_qa_identity_previously_bound_on_its_branch()
    {
        await using var db = Db();
        await Seed(db);
        var central = new WhatsAppChannelSetting { Id = 42, PhoneNumberId = "central" };
        (await db.WhatsAppBranchSettings.SingleAsync(x => x.Id == 1)).PhoneNumberId = "456";
        db.AddRange(central,
            new WhatsAppConversation { BranchId = 1, BranchLineId = 1, WhatsAppUserId = "US.allowed", PhoneNumber = "573000000001" },
            new WhatsAppConversation { BranchId = 1, BranchLineId = 1, WhatsAppUserId = "US.outside", PhoneNumber = "573000000009" },
            new WhatsAppConversation { BranchId = 2, BranchLineId = 2, WhatsAppUserId = "US.anotherline", PhoneNumber = "573000000002" },
            new WhatsAppConversation { BranchId = 1, ChannelSettingId = central.Id, WhatsAppUserId = "US.central", PhoneNumber = "573000000002" });
        await db.SaveChangesAsync();
        var pilot = CreateRecoveryPilot(new ServiceCollection(), RecoverySettings(temporary: false));
        var candidateEnvelopes = WhatsAppWebhookEnvelope.Split(Payload("smb_message_echoes", new[]
        {
            EchoRecipient("one", "US.allowed"), EchoRecipient("two", "US.outside"), EchoRecipient("three", "US.anotherline"),
            EchoRecipient("four", "US.central"), EchoRecipient("five", "US.outside", "573000000001")
        }, "456"), matchContactOnly: true);
        var accepted = Assert.Single(await pilot.FilterWebhookAsync(db, candidateEnvelopes, default));
        Assert.Equal("US.allowed", accepted.UserId);
        Assert.Null(accepted.PhoneNumber);
    }

    [Fact]
    public async Task Phone_echo_uses_the_coexistence_line_when_the_legacy_line_has_the_same_customer()
    {
        await using var db = Db();
        await Seed(db);
        db.Add(new WhatsAppBranchSetting { Id = 3, BranchId = 1, PhoneNumberId = "legacy", IsActive = true, IsVerified = true });
        db.Add(new WhatsAppConversation { Id = 10, BranchId = 1, PhoneNumber = "573000000001" });
        await db.SaveChangesAsync();

        await Processor(db, new(), new()).ProcessAsync(new WhatsAppWebhookEvent(),
            Payload("smb_message_echoes", new[] { Echo() }), default);

        var message = await db.WhatsAppMessages.SingleAsync();
        var conversation = await db.WhatsAppConversations.SingleAsync(x => x.Id == message.ConversationId);
        Assert.Equal(1, conversation.BranchLineId);
        Assert.Equal("573000000001", conversation.PhoneNumber);
        Assert.Null((await db.WhatsAppConversations.SingleAsync(x => x.Id == 10)).BranchLineId);
    }

    [Fact]
    public async Task Phone_intervention_pauses_automation_without_impersonation_or_reassignment()
    {
        await using var db = Db(); await Seed(db);
        db.WhatsAppConversations.Add(new WhatsAppConversation { Id = 10, BranchId = 1, BranchLineId = 1, PhoneNumber = "573000000001",
            AttentionMode = WhatsAppAttentionMode.Ai, AttentionModeUpdatedAt = Now.AddMinutes(-1) });
        await db.SaveChangesAsync();
        var queue = new Mock<IWhatsAppAiWorkQueue>(); var notifications = new Mock<IWhatsAppNotificationService>();
        var processor = Processor(db, queue, notifications);
        var payload = Payload("smb_message_echoes", new[] { Echo() });
        await processor.ProcessAsync(new WhatsAppWebhookEvent(), payload, default);
        await processor.ProcessAsync(new WhatsAppWebhookEvent(), payload, default);
        var conversation = await db.WhatsAppConversations.SingleAsync();
        Assert.Equal(WhatsAppAttentionMode.Paused, conversation.AttentionMode);
        Assert.Equal(1, conversation.AttentionRevision);
        Assert.Null(conversation.AssignedUserId);
        Assert.Null(conversation.LastCustomerMessageAt);
        var message = await db.WhatsAppMessages.SingleAsync();
        Assert.Equal("businessApp", message.Source); Assert.Null(message.SentByUserId);
        queue.Verify(x => x.TryEnqueue(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        notifications.Verify(x => x.NotifyMessageCreatedAsync(1, It.IsAny<WhatsAppConversationDto>(), It.IsAny<WhatsAppMessageDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task History_is_deduplicated_without_unread_window_notifications_or_business_effects()
    {
        await using var db = Db(); await Seed(db);
        var queue = new Mock<IWhatsAppAiWorkQueue>(); var notifications = new Mock<IWhatsAppNotificationService>();
        var processor = Processor(db, queue, notifications);
        var payload = Payload("history", new[] { new { metadata = new { phase = 2, chunk_order = 1, progress = 100 },
            threads = new[] { new { id = "573000000001", messages = new[] { new { from = "573000000001", id = "history-1",
                timestamp = "1793300000", type = "text", text = new { body = "quiero comprar" } } } } } } });
        await processor.ProcessAsync(new WhatsAppWebhookEvent(), payload, default);
        await processor.ProcessAsync(new WhatsAppWebhookEvent(), payload, default);
        await processor.ProcessAsync(new WhatsAppWebhookEvent(), payload.Replace("line-1", "line-2"), default);
        Assert.Equal(2, await db.WhatsAppConversations.CountAsync());
        Assert.Equal(2, await db.WhatsAppMessages.CountAsync());
        Assert.All(await db.WhatsAppConversations.ToListAsync(), x => { Assert.Equal(0, x.UnreadCount); Assert.Null(x.LastCustomerMessageAt); Assert.False(x.NeedsHumanResponse); });
        Assert.All(await db.WhatsAppMessages.ToListAsync(), x => Assert.True(x.IsHistorical));
        Assert.Empty(await db.Orders.ToListAsync()); queue.VerifyNoOtherCalls(); notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Contacts_sync_updates_only_channel_contact_and_removal_preserves_history()
    {
        await using var db = Db(); await Seed(db);
        var processor = Processor(db, new(), new());
        await processor.ProcessAsync(new WhatsAppWebhookEvent(), Payload("smb_app_state_sync", new[] {
            new { type = "contact", action = "add", contact = new { phone_number = "573000000001", full_name = "Contacto" } } }), default);
        Assert.Equal("Contacto", (await db.WhatsAppConversations.SingleAsync()).ContactName);
        await processor.ProcessAsync(new WhatsAppWebhookEvent(), Payload("smb_app_state_sync", new[] {
            new { type = "contact", action = "remove", contact = new { phone_number = "573000000001" } } }), default);
        Assert.Null((await db.WhatsAppConversations.SingleAsync()).ContactName);
        Assert.Empty(await db.Customers.ToListAsync());
    }

    [Fact]
    public async Task Offboarding_disables_only_the_matching_line_and_invalidates_automation()
    {
        await using var db = Db(); await Seed(db);
        var line = await db.WhatsAppBranchSettings.SingleAsync(x => x.Id == 1); line.BusinessAccountId = "123";
        db.WhatsAppConversations.Add(new WhatsAppConversation { BranchId = 1, BranchLineId = 1, PhoneNumber = "573000000001", AttentionMode = WhatsAppAttentionMode.Ai });
        await db.SaveChangesAsync();
        var payload = JsonSerializer.Serialize(new { @object = "whatsapp_business_account", entry = new[] {
            new { id = "123", time = 1793901600, changes = new[] { new { field = "account_update", value = new { @event = "PARTNER_REMOVED" } } } } } });
        Assert.Single(WhatsAppWebhookEnvelope.Split(payload));
        await Processor(db, new(), new()).ProcessAsync(new WhatsAppWebhookEvent(), payload, default);
        Assert.False(line.IsVerified); Assert.Equal("disconnected", line.ConnectionState);
        Assert.True((await db.WhatsAppBranchSettings.SingleAsync(x => x.Id == 2)).IsVerified);
        var conversation = await db.WhatsAppConversations.SingleAsync();
        Assert.Equal(WhatsAppAttentionMode.Paused, conversation.AttentionMode); Assert.Equal(1, conversation.AttentionRevision);
    }

    [Fact]
    public async Task Permissions_must_target_the_selected_account_and_meta_errors_do_not_expose_tokens()
    {
        var client = MetaClient(_ => JsonSerializer.Serialize(new { data = new {
            is_valid = true, app_id = "10", scopes = new[] { "whatsapp_business_management", "whatsapp_business_messaging" },
            granular_scopes = new[] { new { scope = "whatsapp_business_management", target_ids = new[] { "other" } } }
        } }));
        await Assert.ThrowsAsync<BusinessException>(() => client.VerifyPermissionsAsync("test-token", "123", default));
        var failed = MetaClient(_ => "secret-test-token", HttpStatusCode.BadRequest);
        var error = await Assert.ThrowsAsync<BusinessException>(() => failed.ExchangeAsync("temporary-code", default));
        Assert.DoesNotContain("secret-test-token", error.Message); Assert.DoesNotContain("temporary-code", error.Message);
    }

    [Fact]
    public async Task Connection_requires_both_platforms_and_all_webhook_fields()
    {
        var client = MetaClient(_ => "{\"data\":[{\"id\":\"456\",\"is_on_biz_app\":true,\"platform_type\":\"OTHER\"}]}");
        await Assert.ThrowsAsync<BusinessException>(() => client.PhoneAsync("123", "456", "test-token", default));
        var missing = MetaClient(_ => "{\"data\":[{\"object\":\"whatsapp_business_account\",\"fields\":[{\"name\":\"messages\"}]}]}");
        await Assert.ThrowsAsync<BusinessException>(() => missing.VerifyWebhookFieldsAsync(default));
    }

    [Fact]
    public async Task Signup_without_phone_id_selects_the_requested_number_within_a_multi_line_account()
    {
        var client = MetaClient(_ => JsonSerializer.Serialize(new { data = new[] {
            new { id = "456", display_phone_number = "+57 311 1111111", is_on_biz_app = true, platform_type = "CLOUD_API" },
            new { id = "789", display_phone_number = "+57 322 2222222", is_on_biz_app = true, platform_type = "CLOUD_API" }
        } }));
        var selected = await client.PhoneAsync("123", null, "test-token", default, "573222222222");
        Assert.Equal("789", selected.GetProperty("id").GetString());
        await Assert.ThrowsAsync<BusinessException>(() => client.PhoneAsync("123", "456", "test-token", default, "573222222222"));
    }

    [Fact]
    public void Pilot_split_uses_matched_contacts_and_does_not_copy_unrelated_contact_data()
    {
        var envelopes = WhatsAppWebhookEnvelope.Split("""
            {"entry":[{"id":"100","changes":[{"field":"messages","value":{
                "metadata":{"phone_number_id":"200"},
                "contacts":[{"wa_id":"573100000001","profile":{"name":"Unrelated"}},
                            {"wa_id":"573100000002","profile":{"name":"Allowed"}}],
                "messages":[{"id":"one","from":"573100000002"},{"id":"two","from_user_id":"unmatched"}]
            }}]}]}
            """, matchContactOnly: true);

        Assert.Equal(2, envelopes.Count);
        Assert.Equal("messages", envelopes[0].Field);
        Assert.Equal("573100000002", envelopes[0].PhoneNumber);
        Assert.Contains("Allowed", envelopes[0].Payload);
        Assert.DoesNotContain("Unrelated", envelopes[0].Payload);
        Assert.Null(envelopes[1].PhoneNumber);
    }

    [Fact]
    public async Task Pilot_keeps_an_existing_meta_subscription_without_resubscribing()
    {
        var writes = 0;
        var meta = MetaClient(request =>
        {
            if (request.Method == HttpMethod.Post) writes++;
            return """{"data":[{"whatsapp_business_api_data":{"id":"10"}}]}""";
        });

        await meta.EnsureSubscriptionAsync("789", "token", CancellationToken.None);
        Assert.Equal(0, writes);
    }

    [Theory]
    [InlineData("USER", "10", true, "123")]
    [InlineData("SYSTEM_USER", "11", true, "123")]
    [InlineData("SYSTEM_USER", "10", false, "123")]
    [InlineData("SYSTEM_USER", "10", true, "999")]
    public async Task Pilot_recovery_requires_a_system_user_token_for_the_configured_app_and_account(
        string type, string appId, bool messaging, string target)
    {
        var client = MetaClient(_ => JsonSerializer.Serialize(new { data = new
        {
            is_valid = true, app_id = appId, type,
            scopes = messaging ? new[] { "whatsapp_business_management", "whatsapp_business_messaging" } : ["whatsapp_business_management"],
            granular_scopes = new[] { new { scope = "whatsapp_business_management", target_ids = new[] { target } } }
        } }));
        await Assert.ThrowsAsync<BusinessException>(() => client.VerifyPilotRecoveryPermissionsAsync("recovery-token", "123", default));
    }

    [Fact]
    public async Task Pilot_recovers_verified_coexistence_and_keeps_the_central_channel_separate()
    {
        await using var db = Db();
        await SeedRecoveryAsync(db);
        var attempts = new List<string>();
        var controller = RecoveryController(db, "ready", attempts);
        var initial = ReadRecoveryStatus(await controller.Get(1, default));
        Assert.True(initial.GetProperty("pilotRecoveryAvailable").GetBoolean());
        Assert.IsType<ForbidResult>(await controller.RecoverPilot(2, default));
        Assert.Empty(attempts);

        var result = ReadRecoveryStatus(await controller.RecoverPilot(1, default));
        Assert.Equal("connected", result.GetProperty("state").GetString());
        Assert.False(result.GetProperty("pilotRecoveryAvailable").GetBoolean());
        Assert.DoesNotContain("recovery-token", result.GetRawText());
        Assert.Equal(new[]
        {
            "GET /10/subscriptions", "GET /debug_token", "GET /123/phone_numbers", "GET /456",
            "GET /123/subscribed_apps", "POST /123/subscribed_apps", "GET /123/subscribed_apps"
        }, attempts);
        Assert.IsType<OkObjectResult>(await controller.RecoverPilot(1, default));
        Assert.Equal(7, attempts.Count);

        var line = await db.WhatsAppBranchSettings.SingleAsync(x => x.CoexistenceEnabled);
        Assert.Equal("123", line.BusinessAccountId);
        Assert.Equal("456", line.PhoneNumberId);
        Assert.Equal("skipped", line.HistorySyncStatus);
        Assert.Equal("skipped", line.ContactsSyncStatus);
        Assert.True(line.IsActive && line.IsVerified && line.CoexistenceEnabled);
        Assert.StartsWith("pilot-recovery.0.", line.AccessToken);
        Assert.DoesNotContain("recovery-token", line.AccessToken);
        Assert.Equal("3904", (await db.WhatsAppChannelSettings.SingleAsync()).PhoneNumberId);
        Assert.Equal("3904", (await db.WhatsAppBranchSettings.SingleAsync(x => !x.CoexistenceEnabled)).PhoneNumberId);
    }

    [Theory]
    [InlineData("not_coexisting", 400, 0)]
    [InlineData("other_callback", 409, 0)]
    [InlineData("unconfirmed_subscription", 400, 1)]
    public async Task Pilot_recovery_failure_preserves_the_previous_branch_settings(string graphState, int status, int writes)
    {
        await using var db = Db();
        await SeedRecoveryAsync(db);
        var attempts = new List<string>();
        var controller = RecoveryController(db, graphState, attempts);

        var result = Assert.IsAssignableFrom<ObjectResult>(await controller.RecoverPilot(1, default));
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(writes, attempts.Count(attempt => attempt.StartsWith("POST ", StringComparison.Ordinal)));
        var line = await db.WhatsAppBranchSettings.AsNoTracking().SingleAsync();
        Assert.Equal("3904", line.PhoneNumberId);
        Assert.Equal("pending", line.ConnectionState);
        Assert.False(line.CoexistenceEnabled);
        Assert.True(line.IsVerified);
        Assert.Null(line.ConnectionError);
    }

    [Fact]
    public async Task Temporary_recovery_encrypts_credentials_and_upgrades_the_existing_line()
    {
        await using var db = Db();
        await SeedRecoveryAsync(db);
        var temporary = RecoveryController(db, "ready", [], temporary: true);
        Assert.Equal("connected", ReadRecoveryStatus(await temporary.RecoverPilot(1, default)).GetProperty("state").GetString());
        var recovered = await db.WhatsAppBranchSettings.SingleAsync(x => x.CoexistenceEnabled);
        var recoveredId = recovered.Id;
        Assert.StartsWith("pilot-recovery.", recovered.AccessToken);
        Assert.DoesNotContain("recovery-token", recovered.AccessToken);

        var expired = RecoveryController(db, "ready", [], temporary: true, clockNow: Now.AddHours(2));
        Assert.Equal("degraded", ReadRecoveryStatus(await expired.Get(1, default)).GetProperty("state").GetString());

        var attempts = new List<string>();
        var durable = RecoveryController(db, "already_subscribed", attempts);
        Assert.True(ReadRecoveryStatus(await durable.Get(1, default)).GetProperty("pilotRecoveryAvailable").GetBoolean());
        Assert.Equal("connected", ReadRecoveryStatus(await durable.RecoverPilot(1, default)).GetProperty("state").GetString());
        var upgraded = await db.WhatsAppBranchSettings.SingleAsync(x => x.CoexistenceEnabled);
        Assert.Equal(recoveredId, upgraded.Id);
        Assert.StartsWith("pilot-recovery.0.", upgraded.AccessToken);
        Assert.DoesNotContain("recovery-token", upgraded.AccessToken);
        Assert.DoesNotContain(attempts, attempt => attempt.StartsWith("POST ", StringComparison.Ordinal));
        Assert.Equal("3904", (await db.WhatsAppChannelSettings.SingleAsync()).PhoneNumberId);
    }

    [Theory]
    [InlineData("too_long")]
    [InlineData("expired")]
    [InlineData("extra_scope")]
    [InlineData("missing_santander")]
    public async Task Temporary_recovery_rejects_tokens_outside_the_local_scope(string graphState)
    {
        await using var db = Db();
        await SeedRecoveryAsync(db);
        var attempts = new List<string>();
        var controller = RecoveryController(db, graphState, attempts, temporary: true);
        Assert.IsType<BadRequestObjectResult>(await controller.RecoverPilot(1, default));
        Assert.Equal(new[] { "GET /10/subscriptions", "GET /debug_token" }, attempts);
        Assert.False((await db.WhatsAppBranchSettings.AsNoTracking().SingleAsync()).CoexistenceEnabled);
    }

    [Fact]
    public async Task Recovered_credential_egress_requires_the_pinned_assets_and_test_recipients()
    {
        var settings = RecoverySettings(temporary: true);
        var protector = new IntegrationSecretProtector(settings);
        var expiry = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
        var credential = $"pilot-recovery.{expiry}.{protector.Protect($"{expiry}.recovery-token")}";
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationSecretProtector>(protector);
        services.AddHttpClient(nameof(WhatsAppCoexistenceClient));
        services.AddHttpClient(nameof(IWhatsAppCloudClient));
        WhatsAppCoexistencePilot.Configure(services, settings, Mock.Of<IHostEnvironment>(x => x.EnvironmentName == "Development"));
        services.PostConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(builder =>
        {
            var pilotHandler = Assert.IsAssignableFrom<DelegatingHandler>(builder.PrimaryHandler);
            pilotHandler.InnerHandler!.Dispose();
            pilotHandler.InnerHandler = new MetaHandler(request =>
            {
                Assert.Equal("recovery-token", request.Headers.Authorization?.Parameter);
                return "{}";
            }, HttpStatusCode.OK);
        }));
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var management = factory.CreateClient(nameof(WhatsAppCoexistenceClient));
        using var cloud = factory.CreateClient(nameof(IWhatsAppCloudClient));
        Assert.Equal(HttpStatusCode.OK, await Send(management, "123/phone_numbers", credential));
        Assert.Equal(HttpStatusCode.Forbidden, await Send(management, "3904/phone_numbers", "recovery-token"));
        Assert.Equal(HttpStatusCode.Forbidden, await Send(management, "3904/subscribed_apps", credential));
        Assert.Equal(HttpStatusCode.OK, await Send(cloud, "456/messages", credential, "573000000001"));
        Assert.Equal(HttpStatusCode.Forbidden, await Send(cloud, "3904/messages", credential, "573000000001"));
        Assert.Equal(HttpStatusCode.Forbidden, await Send(cloud, "456/messages", credential, "573000000003"));

        static async Task<HttpStatusCode> Send(HttpClient client, string path, string token, string? recipient = null)
        {
            using var request = new HttpRequestMessage(recipient is null ? HttpMethod.Get : HttpMethod.Post,
                $"https://graph.facebook.com/v27.0/{path}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (recipient is not null) request.Content = JsonContent.Create(new { to = recipient, type = "text" });
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }
    }

    private static async Task SeedRecoveryAsync(ApplicationDbContext db)
    {
        db.AddRange(new Branch { Id = 1, Name = "Santander" }, new Branch { Id = 2, Name = "Otra" },
            new WhatsAppBranchSetting { BranchId = 1, PhoneNumberId = "3904", IsActive = true, IsVerified = true },
            new WhatsAppChannelSetting { PhoneNumberId = "3904", IsActive = true, IsVerified = true });
        await db.SaveChangesAsync();
    }

    private static JsonElement ReadRecoveryStatus(IActionResult result) =>
        JsonSerializer.SerializeToElement(Assert.IsType<ApiResponse<object>>(Assert.IsType<OkObjectResult>(result).Value).Data);

    private static IConfiguration RecoverySettings(bool temporary) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Database=senor_arroz_coexistence_pilot",
            ["WhatsAppProcessing:Mode"] = "External",
            ["WhatsAppCoexistence:AppSecret"] = new string('a', 32),
            ["WhatsAppCoexistence:WebhookVerifyToken"] = new string('b', 32),
            ["WhatsAppCoexistence:Pilot:Enabled"] = "true",
            ["WhatsAppCoexistence:Pilot:TenantId"] = "1",
            ["WhatsAppCoexistence:Pilot:BranchId"] = "1",
            ["WhatsAppCoexistence:Pilot:PhoneNumberId"] = "456",
            ["WhatsAppCoexistence:Pilot:CallbackUrl"] = "https://pilot.example.com/api/whatsapp/webhook",
            ["WhatsAppCoexistence:Pilot:AllowedRecipients"] = "573000000001,573000000002",
            ["WhatsAppCoexistence:Pilot:RecoveryWabaId"] = "123",
            ["WhatsAppCoexistence:Pilot:RecoveryAccessToken"] = "recovery-token",
            ["WhatsAppCoexistence:Pilot:TemporaryRecoveryToken"] = temporary.ToString(),
            ["Integrations:EncryptionKey"] = new string('k', 32)
        }).Build();

    private static WhatsAppCoexistenceController RecoveryController(ApplicationDbContext db, string graphState, List<string> attempts,
        bool temporary = false, DateTime? clockNow = null)
    {
        var settings = RecoverySettings(temporary);
        var pilot = CreateRecoveryPilot(new ServiceCollection(), settings);
        var subscribed = graphState == "already_subscribed";
        var meta = MetaClient(request =>
        {
            var path = request.RequestUri!.AbsolutePath["/v27.0".Length..];
            var operation = $"{request.Method} {path}";
            attempts.Add(operation);
            if (operation is "POST /123/subscribed_apps")
                subscribed = graphState != "unconfirmed_subscription";
            return operation switch
            {
                "GET /10/subscriptions" => """{"data":[{"object":"whatsapp_business_account","fields":["messages","smb_message_echoes","account_update"]}]}""",
                "GET /debug_token" => temporary ? TemporaryTokenResponse(graphState)
                    : """{"data":{"is_valid":true,"app_id":"10","type":"SYSTEM_USER","scopes":["whatsapp_business_management","whatsapp_business_messaging"]}}""",
                "GET /123/phone_numbers" => JsonSerializer.Serialize(new { data = new[] { new {
                    id = "456", display_phone_number = "+57 301 227 4655", is_on_biz_app = graphState != "not_coexisting", platform_type = "CLOUD_API" } } }),
                "GET /456" => graphState == "other_callback"
                    ? """{"id":"456","webhook_configuration":{"phone_number":"https://other.example.com/webhook"}}"""
                    : """{"id":"456","webhook_configuration":{}}""",
                "GET /123/subscribed_apps" => subscribed
                    ? """{"data":[{"whatsapp_business_api_data":{"id":"10"}}]}""" : """{"data":[]}""",
                "POST /123/subscribed_apps" => """{"success":true}""",
                _ => throw new InvalidOperationException($"Operación de prueba inesperada: {operation}.")
            };
        });
        var actor = Mock.Of<ICurrentUser>(x => x.Id == 7 && x.Role == "Admin" && x.BranchId == 1 && x.IsAuthenticated);
        var now = clockNow ?? Now;
        return new WhatsAppCoexistenceController(db, TestTenantContext.Default, actor, Mock.Of<IClock>(x => x.UtcNow == now),
            new IntegrationSecretProtector(settings), meta, pilot);
    }

    private static WhatsAppCoexistencePilot CreateRecoveryPilot(IServiceCollection services, IConfiguration settings)
    {
        WhatsAppCoexistencePilot.Configure(services, settings, Mock.Of<IHostEnvironment>(x => x.EnvironmentName == "Development"));
        var descriptor = Assert.Single(services, x => x.ServiceType == typeof(WhatsAppCoexistencePilot));
        return Assert.IsType<WhatsAppCoexistencePilot>(descriptor.ImplementationInstance);
    }

    private static string TemporaryTokenResponse(string graphState)
    {
        string[] scopes = ["whatsapp_business_management", "whatsapp_business_messaging", "public_profile"];
        if (graphState == "extra_scope") scopes = [.. scopes, "business_management"];
        var expiry = graphState switch { "too_long" => Now.AddHours(25), "expired" => Now, _ => Now.AddHours(1) };
        return JsonSerializer.Serialize(new { data = new
        {
            is_valid = true, app_id = "10", type = "USER", expires_at = new DateTimeOffset(expiry).ToUnixTimeSeconds(), scopes,
            granular_scopes = scopes.Take(2).Select(scope => new { scope,
                target_ids = graphState == "missing_santander" ? new[] { "3904" } : ["123", "3904"] })
        } });
    }

    internal static WhatsAppCoexistenceClient MetaClient(Func<HttpRequestMessage, string> response, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new HttpClient(new MetaHandler(response, status)), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["WhatsAppCoexistence:AppId"] = "10", ["WhatsAppCoexistence:ConfigurationId"] = "20",
            ["WhatsAppCoexistence:AppSecret"] = new string('a', 32), ["WhatsAppCoexistence:WebhookVerifyToken"] = "test-verify",
            ["WhatsAppCoexistence:GraphApiVersion"] = "v27.0"
        }).Build());
    private sealed class MetaHandler(Func<HttpRequestMessage, string> response, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https", request.RequestUri!.Scheme); Assert.Equal("graph.facebook.com", request.RequestUri.Host);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(response(request)) });
        }
    }
}
