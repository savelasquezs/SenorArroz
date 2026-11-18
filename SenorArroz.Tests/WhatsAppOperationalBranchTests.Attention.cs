using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using SenorArroz.API.Hubs;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public sealed partial class WhatsAppOperationalBranchTests
{
    [Fact]
    public async Task Direct_conversation_read_preserves_remote_assignment_and_read_markers()
    {
        await using var db = CreateDb(); await SeedCentralConversationAsync(db, 2, 7, 3);
        var own = await CreateController(db).GetConversation(11, default);
        var response = Assert.IsType<ApiResponse<WhatsAppConversationDto>>(Assert.IsType<OkObjectResult>(own.Result).Value);
        Assert.Equal(7, response.Data!.AssignedUserId); Assert.Equal(3, response.Data.UnreadCount);
        Assert.Equal(3, (await db.WhatsAppConversations.SingleAsync()).UnreadCount);
        Assert.IsType<ForbidResult>((await CreateController(db, 8).GetConversation(11, default)).Result);
    }

    [Fact]
    public async Task Branch_line_transfer_never_broadcasts_to_the_central_unrouted_group()
    {
        await using var db = CreateDb(); await SeedCentralConversationAsync(db, 1, 7);
        var conversation = await db.WhatsAppConversations.SingleAsync();
        conversation.ChannelSettingId = null; conversation.OperationalBranchId = null; conversation.AssignedUserId = 8;
        await db.SaveChangesAsync();
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.Groups(It.IsAny<IReadOnlyList<string>>())).Returns(proxy.Object);
        var hub = new Mock<IHubContext<WhatsAppHub>>(); hub.SetupGet(x => x.Clients).Returns(clients.Object);
        var notifications = new WhatsAppNotificationService(hub.Object, db, TestTenantContext.Default, NullLogger<WhatsAppNotificationService>.Instance);
        await notifications.NotifyConversationRoutingChangedAsync(1, null, 7, WhatsAppConversationMapper.ToDto(conversation), default);
        clients.Verify(x => x.Groups(It.Is<IReadOnlyList<string>>(g => g.Contains("Tenant_1_WhatsApp_Unassigned"))), Times.Never);
        clients.Verify(x => x.Groups(It.Is<IReadOnlyList<string>>(g => g.Contains("Tenant_1_Branch_1_WhatsApp"))), Times.Once);
    }

    [Fact]
    public async Task Approved_template_uses_conversation_line_after_window_expires_and_never_changes_routing()
    {
        await using var db = CreateDb(); await SeedCentralConversationAsync(db, 2, 7);
        var conversation = await db.WhatsAppConversations.SingleAsync();
        var lastCustomerMessage = DateTime.UtcNow.AddDays(-5);
        conversation.LastCustomerMessageAt = lastCustomerMessage; conversation.NeedsHumanResponse = true;
        (await db.WhatsAppChannelSettings.SingleAsync()).BusinessAccountId = "central-account";
        await db.SaveChangesAsync();
        var cloud = new Mock<IWhatsAppCloudClient>();
        cloud.Setup(x => x.GetMessageTemplatesAsync("central-account", "token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppCloudTemplateSyncResult(true, [
                new("approved", "recontactar", "es", "UTILITY", "APPROVED", "[{\"type\":\"BODY\",\"text\":\"Hola\"}]"),
                new("pending", "pendiente", "es", "UTILITY", "PENDING", "[]")], null));
        cloud.Setup(x => x.SendTemplateMessageAsync("phone", "token", It.IsAny<string>(), "recontactar", "es", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppCloudSendResult(true, "template-message", null));
        var controller = CreateController(db, cloud: cloud.Object);
        Assert.IsType<BadRequestObjectResult>((await controller.SendMessage(11, new() { Text = "Fuera de ventana" }, default)).Result);
        var templates = await controller.GetConversationTemplates(11, default);
        var response = Assert.IsType<ApiResponse<IReadOnlyList<WhatsAppCloudTemplate>>>(Assert.IsType<OkObjectResult>(templates.Result).Value);
        Assert.Single(response.Data!);
        Assert.IsType<OkObjectResult>((await controller.SendConversationTemplate(11, new() { TemplateName = "recontactar", Language = "es" }, default)).Result);
        var sent = await db.WhatsAppMessages.SingleAsync();
        Assert.Equal(11, sent.ConversationId); Assert.Equal(7, sent.SentByUserId); Assert.Equal("template-message", sent.WhatsAppMessageId);
        Assert.Equal(2, conversation.OperationalBranchId); Assert.Equal(7, conversation.AssignedUserId);
        Assert.Equal(lastCustomerMessage, conversation.LastCustomerMessageAt); Assert.False(conversation.NeedsHumanResponse);
        Assert.IsType<ForbidResult>((await CreateController(db, 8, cloud: cloud.Object).SendConversationTemplate(11, new() { TemplateName = "recontactar", Language = "es" }, default)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.SendConversationTemplate(11, new() { TemplateName = "pendiente", Language = "es" }, default)).Result);
        cloud.Verify(x => x.SendTemplateMessageAsync("phone", "token", It.IsAny<string>(), "recontactar", "es", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Attention_requires_version_and_conflict_returns_current_owner()
    {
        await using var db = CreateDb(); await SeedCentralConversationAsync(db, 1, 7);
        var controller = CreateController(db, 8);
        var missing = await controller.ExecuteAttentionAction(11, new() { Action = "take" }, default);
        Assert.Equal(428, Assert.IsType<ObjectResult>(missing.Result).StatusCode);
        var conflict = await controller.ExecuteAttentionAction(11, new() { Action = "take", ExpectedVersion = 0 }, default);
        var response = Assert.IsType<ApiResponse<WhatsAppAttentionDto>>(Assert.IsType<ConflictObjectResult>(conflict.Result).Value);
        Assert.Equal(7, response.Data!.AssignedUserId);
        Assert.Empty(await db.WhatsAppWebhookEvents.ToListAsync());
    }

    [Fact]
    public async Task Release_and_take_are_explicit_and_stale_actions_cannot_steal_conversation()
    {
        await using var db = CreateDb(); await SeedCentralConversationAsync(db, 1, 7);
        var released = await CreateController(db).ExecuteAttentionAction(11, new() { Action = "release", ExpectedVersion = 0 }, default);
        Assert.IsType<OkObjectResult>(released.Result);
        var conversation = await db.WhatsAppConversations.SingleAsync();
        Assert.Null(conversation.AssignedUserId); Assert.True(conversation.NeedsHumanResponse);
        Assert.Equal(1, conversation.AttentionRevision);
        Assert.IsType<ConflictObjectResult>((await CreateController(db, 8).ExecuteAttentionAction(11, new() { Action = "take", ExpectedVersion = 0 }, default)).Result);
        Assert.IsType<OkObjectResult>((await CreateController(db, 8).ExecuteAttentionAction(11, new() { Action = "take", ExpectedVersion = 1 }, default)).Result);
        Assert.Equal(8, conversation.AssignedUserId); Assert.Equal(2, conversation.AttentionRevision);
        Assert.Equal(2, await db.WhatsAppWebhookEvents.CountAsync(x => x.EventType == "attention_action"));
    }

    [Fact]
    public async Task Force_take_requires_supervisor_and_reason_and_preserves_routing()
    {
        await using var db = CreateDb(); await SeedCentralConversationAsync(db, 1, 7);
        var request = new WhatsAppAttentionActionDto { Action = "force-take", ExpectedVersion = 0, Reason = "Relevo autorizado" };
        Assert.IsType<ForbidResult>((await CreateController(db, 8).ExecuteAttentionAction(11, request, default)).Result);
        request.Reason = null;
        Assert.IsType<BadRequestObjectResult>((await CreateController(db, 8, role: "Admin").ExecuteAttentionAction(11, request, default)).Result);
        request.Reason = "Relevo autorizado";
        Assert.IsType<OkObjectResult>((await CreateController(db, 8, role: "Admin").ExecuteAttentionAction(11, request, default)).Result);
        var conversation = await db.WhatsAppConversations.SingleAsync();
        Assert.Equal(8, conversation.AssignedUserId); Assert.Equal(1, conversation.OperationalBranchId);
        Assert.Contains("Relevo autorizado", (await db.WhatsAppWebhookEvents.SingleAsync()).RawPayload);
    }

    [Fact]
    public async Task Transfer_rejects_ineligible_advisor_and_does_not_change_order_destination()
    {
        await using var db = CreateDb(); await SeedCentralConversationAsync(db, 1, 7);
        db.Users.AddRange(new User { Id = 8, BranchId = 1, Role = UserRole.Cashier, Name = "Asesor" },
            new User { Id = 9, BranchId = 2, Role = UserRole.Cashier, Name = "Otra sede" });
        await db.SaveChangesAsync();
        var controller = CreateController(db);
        Assert.IsType<BadRequestObjectResult>((await controller.ExecuteAttentionAction(11, new() { Action = "transfer", ExpectedVersion = 0, TargetUserId = 9 }, default)).Result);
        Assert.IsType<OkObjectResult>((await controller.ExecuteAttentionAction(11, new() { Action = "transfer", ExpectedVersion = 0, TargetUserId = 8 }, default)).Result);
        var conversation = await db.WhatsAppConversations.SingleAsync();
        Assert.Equal(8, conversation.AssignedUserId); Assert.Equal(1, conversation.BranchId); Assert.Equal(1, conversation.OperationalBranchId);
    }

    [Fact]
    public async Task Reading_does_not_resolve_human_response_and_nonowner_cannot_send_or_link_customer()
    {
        await using var db = CreateDb(); await SeedCentralConversationAsync(db, 1, 7, 3);
        var conversation = await db.WhatsAppConversations.SingleAsync(); conversation.NeedsHumanResponse = true;
        await db.SaveChangesAsync();
        var reader = CreateController(db, 8);
        Assert.IsType<OkObjectResult>((await reader.GetMessages(11, default)).Result);
        Assert.Equal(0, conversation.UnreadCount); Assert.True(conversation.NeedsHumanResponse);
        Assert.IsType<ForbidResult>((await reader.SendMessage(11, new() { Text = "No debe enviarse" }, default)).Result);
        Assert.Empty(await db.WhatsAppMessages.ToListAsync()); Assert.Equal(7, conversation.AssignedUserId);
    }

    [Theory]
    [InlineData("free", 0)] [InlineData("mine", 1)] [InlineData("team", 0)] [InlineData("closed", 0)]
    public async Task Inbox_filters_apply_on_server_before_the_result_limit(string inbox, int expected)
    {
        await using var db = CreateDb(); await SeedCentralConversationAsync(db, 1, 7);
        var result = await CreateController(db).GetConversations(new() { Inbox = inbox }, default);
        var response = Assert.IsType<ApiResponse<IReadOnlyList<WhatsAppConversationDto>>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(expected, response.Data!.Count);
    }
}
