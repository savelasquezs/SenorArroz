using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using AutoMapper;
using SenorArroz.API.Controllers;
using SenorArroz.API.Hubs;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Services;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;
using System.Text.Json;

namespace SenorArroz.Tests;

public sealed partial class WhatsAppOperationalBranchTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(8)]
    public async Task Forwarding_to_an_unowned_chat_requires_access_and_preserves_native_location_content(int? assignee)
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, operationalBranchId: 1, assignedUserId: assignee);
        var destination = await db.WhatsAppConversations.SingleAsync();
        destination.LastCustomerMessageAt = DateTime.UtcNow;
        destination.NeedsHumanResponse = true;
        var origin = new WhatsAppConversation { Id = 12, TenantId = 1, ChannelSettingId = 1, BranchId = 1, OperationalBranchId = 2 };
        db.WhatsAppConversations.Add(origin);
        var message = new WhatsAppMessage
        {
            ConversationId = origin.Id, TenantId = 1, TextBody = "Ubicación compartida", Direction = WhatsAppMessageDirection.Inbound,
            RawPayload = """{"type":"location","from":"573000000001","location":{"latitude":6.26,"longitude":-75.56,"name":"Punto"}}"""
        };
        db.WhatsAppMessages.Add(message);
        await db.SaveChangesAsync();
        var provider = new Mock<IWhatsAppCloudClient>();
        provider.Setup(x => x.SendSharedContentAsync("phone", "token", It.IsAny<string>(), "location",
            It.Is<JsonElement>(j => j.GetProperty("latitude").GetDouble() == 6.26), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppCloudSendResult(true, "wamid.forwarded", null));
        var controller = CreateController(db, cloud: provider.Object);
        var request = new WhatsAppForwardMessageDto(origin.Id, message.Id);
        Assert.IsType<ForbidResult>((await controller.ForwardMessage(destination.Id, request, default)).Result);
        provider.VerifyNoOtherCalls();

        origin.OperationalBranchId = 1;
        destination.OperationalBranchId = 2;
        await db.SaveChangesAsync();
        Assert.IsType<ForbidResult>((await controller.ForwardMessage(destination.Id, request, default)).Result);
        destination.OperationalBranchId = 1;
        destination.AttentionMode = WhatsAppAttentionMode.Closed;
        await db.SaveChangesAsync();
        Assert.IsType<BadRequestObjectResult>((await controller.ForwardMessage(destination.Id, request, default)).Result);
        var activeMode = assignee.HasValue ? WhatsAppAttentionMode.Human : WhatsAppAttentionMode.WaitingForHuman;
        destination.AttentionMode = activeMode;
        destination.LastCustomerMessageAt = DateTime.UtcNow.AddHours(-25);
        await db.SaveChangesAsync();
        Assert.IsType<BadRequestObjectResult>((await controller.ForwardMessage(destination.Id, request, default)).Result);
        provider.VerifyNoOtherCalls();
        destination.LastCustomerMessageAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        Assert.IsType<ForbidResult>((await controller.SendMessage(destination.Id, new() { Text = "Mensaje directo" }, default)).Result);
        var response = Assert.IsType<OkObjectResult>((await controller.ForwardMessage(destination.Id, request, default)).Result);
        var forwarded = Assert.IsType<ApiResponse<WhatsAppMessageDto>>(response.Value).Data!;
        Assert.True(forwarded.IsForwarded);
        Assert.Equal("location", forwarded.Type);
        Assert.Equal("Punto", forwarded.Location?.Name);
        Assert.Null(forwarded.ReplyToWhatsAppMessageId);
        Assert.Equal(destination.Id, forwarded.ConversationId);
        Assert.Equal(assignee, destination.AssignedUserId);
        Assert.Equal(activeMode, destination.AttentionMode);
        Assert.True(destination.NeedsHumanResponse);
    }

    [Fact]
    public async Task Sticker_download_checks_conversation_access_and_message_visibility()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, operationalBranchId: 1, assignedUserId: 7);
        const string url = "https://storage.googleapis.com/test-bucket/sticker.webp";
        var bytes = Convert.FromBase64String("UklGRi4AAABXRUJQVlA4TCIAAAAv/8F/AAfQlAIVsv8BgUCyv/cMRfQ/4z//+c9//vOf//wf");
        var message = new WhatsAppMessage
        {
            ConversationId = 11, TenantId = 1, Direction = WhatsAppMessageDirection.Inbound,
            Type = WhatsAppMessageType.Sticker, MediaUrl = url,
        };
        db.WhatsAppMessages.Add(message);
        await db.SaveChangesAsync();

        var storage = new Mock<IFirebaseGcsStorage>();
        storage.Setup(x => x.DownloadObjectFromUrlAsync(url, 500 * 1024, It.IsAny<CancellationToken>())).ReturnsAsync(bytes);
        var controller = CreateController(db, storage: storage.Object);
        var otherBranch = CreateController(db, userId: 8, branchId: 2, storage: storage.Object);

        Assert.IsType<ForbidResult>(await otherBranch.DownloadSticker(11, message.Id, default));
        var downloaded = Assert.IsType<FileContentResult>(await controller.DownloadSticker(11, message.Id, default));
        Assert.Equal("image/webp", downloaded.ContentType);
        Assert.Equal(bytes, downloaded.FileContents);
        message.HiddenAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await controller.DownloadSticker(11, message.Id, default));
        storage.Verify(x => x.DownloadObjectFromUrlAsync(url, 500 * 1024, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WhatsApp_customer_can_be_linked_by_exact_phone_across_branches()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, operationalBranchId: 1, assignedUserId: 7);
        var chat = await db.WhatsAppConversations.SingleAsync();
        chat.WhatsAppUserId = "CO.test1020";
        chat.WhatsAppUsername = "@nuevo_usuario";
        var customer = new Customer { Id = 104, BranchId = 2, Name = "Cliente registrado", Phone1 = "3001234567", Active = true };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var controller = CreateController(db);
        var wrongPhone = await controller.LinkCustomer(11, new() { CustomerId = 104, PhoneNumber = "3001234568" }, default);
        Assert.IsType<BadRequestObjectResult>(wrongPhone.Result);
        var stale = await controller.LinkCustomer(11, new() { CustomerId = 104, PhoneNumber = "3001234567", ExpectedAttentionVersion = 4 }, default);
        Assert.IsType<ConflictObjectResult>(stale.Result);
        var linked = await controller.LinkCustomer(11, new() { CustomerId = 104, PhoneNumber = "+57 300 123 4567", ExpectedAttentionVersion = 0 }, default);
        Assert.IsType<OkObjectResult>(linked.Result);
        Assert.Equal(customer.Id, chat.CustomerId);
        Assert.Equal("@nuevo_usuario", customer.WhatsAppUsername);
        Assert.Equal("CO.test1020", customer.WhatsAppUserId);
        Assert.Equal("Cliente registrado", customer.Name);
        Assert.Equal("3001234567", customer.Phone1);
    }

    [Fact]
    public async Task Hiding_a_message_preserves_the_row_and_redacts_content_for_other_cashiers()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, operationalBranchId: 1, assignedUserId: 7);
        db.WhatsAppMessages.Add(new WhatsAppMessage { Id = 92, ConversationId = 11, TextBody = "Contenido oculto",
            MediaUrl = "https://example.invalid/photo", WhatsAppMessageId = "wamid.fixture" });
        await db.SaveChangesAsync();
        var observer = CreateController(db, userId: 8);
        Assert.IsType<ForbidResult>((await observer.HideMessage(11, 92, default)).Result);
        var hidden = await CreateController(db).HideMessage(11, 92, default);
        Assert.IsType<OkObjectResult>(hidden.Result);
        var history = Assert.IsType<ApiResponse<WhatsAppMessagePageDto>>(
            Assert.IsType<OkObjectResult>((await observer.GetMessagesPage(11)).Result).Value);
        var item = Assert.Single(history.Data!.Items);
        Assert.Equal("pos", item.HiddenSource);
        Assert.Empty(item.TextBody);
        Assert.Null(item.MediaUrl);
        Assert.Equal("Contenido oculto", (await db.WhatsAppMessages.SingleAsync()).TextBody);
    }

    [Fact]
    public async Task Message_cursor_handles_equal_timestamps_and_preserves_new_unread_while_loading_older_messages()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, operationalBranchId: 1, assignedUserId: 7, unreadCount: 2);
        var timestamp = new DateTime(2026, 10, 26, 15, 0, 0, DateTimeKind.Utc);
        for (var id = 1; id <= 60; id++) db.WhatsAppMessages.Add(new WhatsAppMessage {
            Id = id, ConversationId = 11, Timestamp = timestamp, TextBody = $"Message {id}" });
        await db.SaveChangesAsync();
        var controller = CreateController(db);
        var first = await controller.GetMessagesPage(11, cancellationToken: default);
        var page = Assert.IsType<ApiResponse<WhatsAppMessagePageDto>>(Assert.IsType<OkObjectResult>(first.Result).Value).Data!;
        Assert.Equal(50, page.Items.Count);
        Assert.Equal(11, page.Items[0].Id);
        Assert.True(page.HasMore);
        var conversation = await db.WhatsAppConversations.SingleAsync();
        conversation.UnreadCount = 1;
        db.WhatsAppMessages.Add(new WhatsAppMessage { Id = 61, ConversationId = 11, Timestamp = timestamp, TextBody = "New" });
        await db.SaveChangesAsync();
        var older = await controller.GetMessagesPage(11, before: page.NextCursor);
        var olderPage = Assert.IsType<ApiResponse<WhatsAppMessagePageDto>>(Assert.IsType<OkObjectResult>(older.Result).Value).Data!;
        Assert.Equal(Enumerable.Range(1, 10), olderPage.Items.Select(item => item.Id));
        Assert.False(olderPage.HasMore);
        Assert.Equal(1, conversation.UnreadCount);
    }

    [Fact]
    public async Task Message_pages_preserve_authorization_and_reject_invalid_bounds()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, operationalBranchId: 2, assignedUserId: 7);
        Assert.IsType<ForbidResult>((await CreateController(db, userId: 8).GetMessagesPage(11)).Result);
        var controller = CreateController(db);
        Assert.IsType<BadRequestObjectResult>((await controller.GetMessagesPage(11, limit: 101)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.GetMessagesPage(11, before: "invalid!")).Result);
    }

    [Fact]
    public async Task Cashier_can_list_and_take_unassigned_central_conversation_without_assigning_branch()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, operationalBranchId: null, assignedUserId: null);
        var controller = CreateController(db);

        var listed = await controller.GetConversations(new WhatsAppConversationSearchDto(), default);
        var listPayload = Assert.IsType<OkObjectResult>(listed.Result).Value as ApiResponse<IReadOnlyList<WhatsAppConversationDto>>;
        var conversation = Assert.Single(listPayload!.Data!);
        Assert.Null(conversation.OperationalBranchId);

        var taken = await controller.ExecuteAttentionAction(conversation.Id, new WhatsAppAttentionActionDto { Action = "take", ExpectedVersion = 0 }, default);
        Assert.IsType<OkObjectResult>(taken.Result);
        var entity = await db.WhatsAppConversations.SingleAsync();
        Assert.Equal(7, entity.AssignedUserId);
        Assert.Null(entity.OperationalBranchId);
    }

    [Fact]
    public async Task Assigning_unassigned_central_conversation_preserves_assigned_cashier()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, operationalBranchId: null, assignedUserId: 7);
        var controller = CreateController(db);

        var result = await controller.UpdateOperationalBranch(11, new UpdateWhatsAppOperationalBranchDto { ExpectedVersion = 0, OperationalBranchId = 2 }, default);

        var payload = Assert.IsType<OkObjectResult>(result.Result).Value as ApiResponse<WhatsAppConversationDto>;
        Assert.Equal(2, payload!.Data!.OperationalBranchId);
        var entity = await db.WhatsAppConversations.SingleAsync();
        Assert.Equal(2, entity.OperationalBranchId);
        Assert.Equal(7, entity.AssignedUserId);
    }

    [Fact]
    public async Task Assigned_cashier_can_list_cross_branch_conversation_and_see_it_in_unread_summary()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, operationalBranchId: 2, assignedUserId: 7, unreadCount: 3);
        var controller = CreateController(db);

        var listed = await controller.GetConversations(new WhatsAppConversationSearchDto(), default);
        var listPayload = Assert.IsType<OkObjectResult>(listed.Result).Value as ApiResponse<IReadOnlyList<WhatsAppConversationDto>>;
        var conversation = Assert.Single(listPayload!.Data!);
        Assert.Equal(2, conversation.OperationalBranchId);
        Assert.Equal(7, conversation.AssignedUserId);

        var summaryResult = await controller.GetUnreadSummary(default);
        var summary = Assert.IsType<OkObjectResult>(summaryResult.Result).Value as ApiResponse<WhatsAppUnreadSummaryDto>;
        Assert.Equal(3, summary!.Data!.TotalUnread);
        Assert.Equal(1, summary.Data.UnreadConversations);
    }

    [Fact]
    public async Task Different_cashier_cannot_access_cross_branch_conversation_assigned_to_someone_else()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, operationalBranchId: 2, assignedUserId: 7, unreadCount: 3);
        var controller = CreateController(db, userId: 8, branchId: 1);

        var listed = await controller.GetConversations(new WhatsAppConversationSearchDto(), default);
        var listPayload = Assert.IsType<OkObjectResult>(listed.Result).Value as ApiResponse<IReadOnlyList<WhatsAppConversationDto>>;
        Assert.Empty(listPayload!.Data!);

        var summaryResult = await controller.GetUnreadSummary(default);
        var summary = Assert.IsType<OkObjectResult>(summaryResult.Result).Value as ApiResponse<WhatsAppUnreadSummaryDto>;
        Assert.Equal(0, summary!.Data!.TotalUnread);
        Assert.Equal(0, summary.Data.UnreadConversations);

        var messages = await controller.GetMessages(11, default);
        Assert.IsType<ForbidResult>(messages.Result);
    }

    [Fact]
    public async Task Unread_summary_sums_visible_conversations_and_picks_latest_message()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, operationalBranchId: 1, assignedUserId: null, unreadCount: 2);
        var initial = await db.WhatsAppConversations.SingleAsync();
        var earlier = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        initial.LastMessageAt = earlier;
        db.WhatsAppConversations.AddRange(
            new WhatsAppConversation
            {
                Id = 12, TenantId = 1, BranchId = 1, ChannelSettingId = 1,
                UnreadCount = 3, LastMessageAt = earlier.AddMinutes(5)
            },
            new WhatsAppConversation
            {
                Id = 13, TenantId = 1, BranchId = 1, ChannelSettingId = 1,
                OperationalBranchId = 2, AssignedUserId = 8, UnreadCount = 20,
                LastMessageAt = earlier.AddHours(1)
            });
        await db.SaveChangesAsync();

        var result = await CreateController(db).GetUnreadSummary(default);
        var payload = Assert.IsType<OkObjectResult>(result.Result).Value as ApiResponse<WhatsAppUnreadSummaryDto>;
        Assert.NotNull(payload?.Data);
        Assert.Equal(5, payload.Data.TotalUnread);
        Assert.Equal(2, payload.Data.UnreadConversations);
        Assert.Equal(earlier.AddMinutes(5), payload.Data.LatestMessageAt);
    }

    [Fact]
    public async Task Transferring_between_operational_branches_clears_assigned_cashier()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, operationalBranchId: 1, assignedUserId: 7);
        var controller = CreateController(db);

        var result = await controller.UpdateOperationalBranch(11, new UpdateWhatsAppOperationalBranchDto { ExpectedVersion = 0, OperationalBranchId = 2 }, default);

        Assert.IsType<OkObjectResult>(result.Result);
        var entity = await db.WhatsAppConversations.SingleAsync();
        Assert.Equal(2, entity.OperationalBranchId);
        Assert.Null(entity.AssignedUserId);

        var listed = await controller.GetConversations(new WhatsAppConversationSearchDto(), default);
        var listPayload = Assert.IsType<OkObjectResult>(listed.Result).Value as ApiResponse<IReadOnlyList<WhatsAppConversationDto>>;
        Assert.Empty(listPayload!.Data!);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(2, false)]
    public async Task Assigned_cashier_can_choose_order_destination_independently_of_conversation_routing(int destination, bool central)
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, operationalBranchId: 2, assignedUserId: 7);
        if (!central)
        {
            var conversation = await db.WhatsAppConversations.SingleAsync();
            conversation.ChannelSettingId = null;
            conversation.ChannelSetting = null;
            await db.SaveChangesAsync();
        }
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.Id).Returns(7);
        currentUser.SetupGet(x => x.Role).Returns("Cashier");
        currentUser.SetupGet(x => x.BranchId).Returns(1);
        var createdOrder = default(Order);
        var repository = new Mock<IOrderRepository>();
        repository.Setup(x => x.CreateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order order, CancellationToken _) =>
            {
                order.Id = 99;
                createdOrder = order;
                return order;
            });
        repository.Setup(x => x.GetByIdWithFullDetailsAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => createdOrder);
        var mapper = new MapperConfiguration(config => config.AddMaps(typeof(CreateOrderCommand).Assembly), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
            .CreateMapper();
        var handler = new CreateOrderHandler(
            repository.Object,
            db,
            mapper,
            currentUser.Object,
            Mock.Of<IOrderNotificationService>(),
            Mock.Of<IClock>(x => x.UtcNow == DateTime.UtcNow));

        await handler.Handle(new CreateOrderCommand
        {
            AllowAssignedWhatsAppOperationalBranch = true,
            Order = new CreateOrderDto
            {
                BranchId = destination,
                WhatsAppConversationId = 11,
                Type = OrderType.Onsite,
                GuestName = "Persona que recoge",
                OrderDetails = [new CreateOrderDetailDto { ProductId = 1, Quantity = 1, UnitPrice = 10000 }]
            }
        }, default);

        Assert.NotNull(createdOrder);
        Assert.Equal(destination, createdOrder!.BranchId);
        Assert.Equal(1, createdOrder.OriginBranchId);
        Assert.Equal(1, currentUser.Object.BranchId);
        Assert.Equal(11, createdOrder.WhatsAppConversationId);
    }

    [Fact]
    public async Task Cashier_cannot_use_assigned_conversation_to_create_order_for_unknown_destination()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, operationalBranchId: 2, assignedUserId: 7);
        var handler = CreateOrderHandler(db, userId: 7, branchId: 1);

        var exception = await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(new CreateOrderCommand
        {
            AllowAssignedWhatsAppOperationalBranch = true,
            Order = OrderRequest(branchId: 999)
        }, default));

        Assert.Contains("sucursal destino", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cashier_cannot_use_conversation_assigned_to_another_user_for_cross_branch_order()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, operationalBranchId: 2, assignedUserId: 8);
        var handler = CreateOrderHandler(db, userId: 7, branchId: 1);

        await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(new CreateOrderCommand
        {
            AllowAssignedWhatsAppOperationalBranch = true,
            Order = OrderRequest(branchId: 2)
        }, default));
    }

    [Fact]
    public async Task Cashier_cannot_confirm_a_local_chat_order_after_another_advisor_takes_ownership()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, operationalBranchId: 1, assignedUserId: 8);
        var conversation = await db.WhatsAppConversations.SingleAsync();
        conversation.AttentionMode = WhatsAppAttentionMode.Human;
        await db.SaveChangesAsync();
        var handler = CreateOrderHandler(db, userId: 7, branchId: 1);

        await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(new CreateOrderCommand
        {
            Order = OrderRequest(branchId: 1)
        }, default));
    }

    [Fact]
    public void Central_realtime_groups_include_assigned_user_without_exposing_original_branch()
    {
        var groups = WhatsAppRealtimeGroupResolver.Resolve(1, 1, new WhatsAppConversationDto
        {
            IsCentralChannel = true,
            OperationalBranchId = 2,
            AssignedUserId = 7
        });

        Assert.Contains("Tenant_1_Branch_2_WhatsApp", groups);
        Assert.Contains("Tenant_1_WhatsApp_Superadmin", groups);
        Assert.Contains("Tenant_1_User_7_WhatsApp", groups);
        Assert.DoesNotContain("Tenant_1_Branch_1_WhatsApp", groups);
    }

    [Fact]
    public void Unassigned_realtime_groups_also_include_assigned_user()
    {
        var groups = WhatsAppRealtimeGroupResolver.Resolve(1, 1, new WhatsAppConversationDto
        {
            IsCentralChannel = true,
            AssignedUserId = 7
        });

        Assert.Equal(["Tenant_1_WhatsApp_Unassigned", "Tenant_1_User_7_WhatsApp"], groups);
    }

    [Fact]
    public void Transfer_reaches_previous_user_once_but_future_groups_exclude_them()
    {
        var previousGroups = WhatsAppRealtimeGroupResolver.Resolve(1, 1, new WhatsAppConversationDto
        {
            IsCentralChannel = true,
            OperationalBranchId = 1,
            AssignedUserId = 7
        });
        var currentGroups = WhatsAppRealtimeGroupResolver.Resolve(1, 1, new WhatsAppConversationDto
        {
            IsCentralChannel = true,
            OperationalBranchId = 2,
            AssignedUserId = null
        });
        var routingChangeGroups = previousGroups.Concat(currentGroups).Distinct().ToArray();

        Assert.Contains("Tenant_1_User_7_WhatsApp", routingChangeGroups);
        Assert.DoesNotContain("Tenant_1_User_7_WhatsApp", currentGroups);
        Assert.Contains("Tenant_1_Branch_2_WhatsApp", currentGroups);
    }

    [Fact]
    public void Equivalent_conversations_from_two_tenants_never_share_realtime_groups()
    {
        var conversation = new WhatsAppConversationDto
        {
            IsCentralChannel = true,
            OperationalBranchId = 2,
            AssignedUserId = 7
        };

        var tenantOne = WhatsAppRealtimeGroupResolver.Resolve(1, 1, conversation);
        var tenantTwo = WhatsAppRealtimeGroupResolver.Resolve(2, 1, conversation);

        Assert.Empty(tenantOne.Intersect(tenantTwo));
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        currentTenant: TestTenantContext.Default,
        tenantExecutionContext: TestTenantContext.Default);

    private static async Task SeedCentralConversationAsync(
        ApplicationDbContext db,
        int? operationalBranchId,
        int? assignedUserId,
        int unreadCount = 0)
    {
        if (db.Database.IsRelational() && assignedUserId is int userId)
            db.Users.Add(new User { Id = userId, BranchId = 1, Role = UserRole.Cashier,
                Name = "Caja", Email = "cashier@demo.test", Phone = "3010000007" });

        db.AddRange(
            new Branch { Id = 1, Name = "Centro", IsActive = true },
            new Branch { Id = 2, Name = "Manrique", IsActive = true },
            new WhatsAppChannelSetting
            {
                Id = 1,
                TenantId = 1,
                IsActive = true,
                IsVerified = true,
                PhoneNumberId = "phone",
                AccessToken = "token"
            },
            new WhatsAppConversation
            {
                Id = 11,
                TenantId = 1,
                ChannelSettingId = 1,
                BranchId = 1,
                OperationalBranchId = operationalBranchId,
                AssignedUserId = assignedUserId,
                UnreadCount = unreadCount,
                AttentionMode = assignedUserId.HasValue ? WhatsAppAttentionMode.Human : WhatsAppAttentionMode.Ai,
                PhoneNumber = "3000000000"
            });
        await db.SaveChangesAsync();
    }

    private static WhatsAppController CreateController(ApplicationDbContext db, int userId = 7, int branchId = 1, string role = "Cashier", IWhatsAppCloudClient? cloud = null, IInventoryService? inventory = null, WhatsAppCloudOptions? cloudOptions = null, IFirebaseGcsStorage? storage = null, IClock? clock = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.Id).Returns(userId);
        currentUser.SetupGet(x => x.Role).Returns(role);
        currentUser.SetupGet(x => x.BranchId).Returns(branchId);
        var branchContext = new Mock<IBranchContext>();
        branchContext.Setup(x => x.RequireBranch(It.IsAny<int?>())).Returns(branchId);
        var notifications = new Mock<IWhatsAppNotificationService>();
        notifications.Setup(x => x.NotifyAttentionChangedAsync(It.IsAny<int>(), It.IsAny<WhatsAppConversationDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        notifications.Setup(x => x.NotifyConversationRoutingChangedAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<WhatsAppConversationDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new WhatsAppController(
            db,
            currentUser.Object,
            branchContext.Object,
            clock ?? Mock.Of<IClock>(x => x.UtcNow == DateTime.UtcNow),
            cloud ?? Mock.Of<IWhatsAppCloudClient>(),
            notifications.Object,
            storage ?? Mock.Of<IFirebaseGcsStorage>(),
            Options.Create(new FirebaseStorageOptions()),
            Options.Create(cloudOptions ?? new()),
            Options.Create(new WhatsAppAiOrchestratorOptions()),
            Mock.Of<ILogger<WhatsAppController>>(),
            new WhatsAppAttentionService(),
            Mock.Of<IWhatsAppAiWorkQueue>(),
            Mock.Of<IWhatsAppAutomaticMessageSender>(),
            Mock.Of<IBranchBusinessHoursService>(),
            null!,
            null!,
            TestTenantContext.Default,
            TestTenantContext.Default,
            inventory: inventory);
    }

    private static CreateOrderHandler CreateOrderHandler(ApplicationDbContext db, int userId, int branchId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.Id).Returns(userId);
        currentUser.SetupGet(x => x.Role).Returns("Cashier");
        currentUser.SetupGet(x => x.BranchId).Returns(branchId);
        var repository = new Mock<IOrderRepository>();
        var mapper = new MapperConfiguration(
            config => config.AddMaps(typeof(CreateOrderCommand).Assembly),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance).CreateMapper();
        return new CreateOrderHandler(
            repository.Object,
            db,
            mapper,
            currentUser.Object,
            Mock.Of<IOrderNotificationService>(),
            Mock.Of<IClock>(x => x.UtcNow == DateTime.UtcNow));
    }

    private static CreateOrderDto OrderRequest(int branchId) => new()
    {
        BranchId = branchId,
        WhatsAppConversationId = 11,
        Type = OrderType.Onsite,
        OrderDetails = [new CreateOrderDetailDto { ProductId = 1, Quantity = 1, UnitPrice = 10000 }]
    };
}
