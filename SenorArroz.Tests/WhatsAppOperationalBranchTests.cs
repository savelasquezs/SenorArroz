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

namespace SenorArroz.Tests;

public sealed class WhatsAppOperationalBranchTests
{
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

        var taken = await controller.TakeConversation(conversation.Id, default);
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

        var result = await controller.UpdateOperationalBranch(11, new UpdateWhatsAppOperationalBranchDto { OperationalBranchId = 2 }, default);

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
    public async Task Transferring_between_operational_branches_clears_assigned_cashier()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, operationalBranchId: 1, assignedUserId: 7);
        var controller = CreateController(db);

        var result = await controller.UpdateOperationalBranch(11, new UpdateWhatsAppOperationalBranchDto { OperationalBranchId = 2 }, default);

        Assert.IsType<OkObjectResult>(result.Result);
        var entity = await db.WhatsAppConversations.SingleAsync();
        Assert.Equal(2, entity.OperationalBranchId);
        Assert.Null(entity.AssignedUserId);

        var listed = await controller.GetConversations(new WhatsAppConversationSearchDto(), default);
        var listPayload = Assert.IsType<OkObjectResult>(listed.Result).Value as ApiResponse<IReadOnlyList<WhatsAppConversationDto>>;
        Assert.Empty(listPayload!.Data!);
    }

    [Fact]
    public async Task Assigned_cashier_can_create_order_for_selected_operational_branch_only()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, operationalBranchId: 2, assignedUserId: 7);
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
                BranchId = 2,
                WhatsAppConversationId = 11,
                Type = OrderType.Onsite,
                OrderDetails = [new CreateOrderDetailDto { ProductId = 1, Quantity = 1, UnitPrice = 10000 }]
            }
        }, default);

        Assert.NotNull(createdOrder);
        Assert.Equal(2, createdOrder!.BranchId);
        Assert.Equal(11, createdOrder.WhatsAppConversationId);
    }

    [Fact]
    public async Task Cashier_cannot_use_conversation_to_create_order_outside_its_operational_branch()
    {
        await using var db = CreateDb();
        await SeedCentralConversationAsync(db, operationalBranchId: 2, assignedUserId: 7);
        var handler = CreateOrderHandler(db, userId: 7, branchId: 1);

        var exception = await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(new CreateOrderCommand
        {
            AllowAssignedWhatsAppOperationalBranch = true,
            Order = OrderRequest(branchId: 1)
        }, default));

        Assert.Contains("No tienes permiso", exception.Message);
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
    public void Central_realtime_groups_include_assigned_user_without_exposing_original_branch()
    {
        var groups = WhatsAppRealtimeGroupResolver.Resolve(1, new WhatsAppConversationDto
        {
            IsCentralChannel = true,
            OperationalBranchId = 2,
            AssignedUserId = 7
        });

        Assert.Contains("Branch_2_WhatsApp", groups);
        Assert.Contains("Tenant_1_WhatsApp_Superadmin", groups);
        Assert.Contains("User_7_WhatsApp", groups);
        Assert.DoesNotContain("Branch_1_WhatsApp", groups);
    }

    [Fact]
    public void Unassigned_realtime_groups_also_include_assigned_user()
    {
        var groups = WhatsAppRealtimeGroupResolver.Resolve(1, new WhatsAppConversationDto
        {
            IsCentralChannel = true,
            AssignedUserId = 7
        });

        Assert.Equal(["Tenant_1_WhatsApp_Unassigned", "User_7_WhatsApp"], groups);
    }

    [Fact]
    public void Transfer_reaches_previous_user_once_but_future_groups_exclude_them()
    {
        var previousGroups = WhatsAppRealtimeGroupResolver.Resolve(1, new WhatsAppConversationDto
        {
            IsCentralChannel = true,
            OperationalBranchId = 1,
            AssignedUserId = 7
        });
        var currentGroups = WhatsAppRealtimeGroupResolver.Resolve(1, new WhatsAppConversationDto
        {
            IsCentralChannel = true,
            OperationalBranchId = 2,
            AssignedUserId = null
        });
        var routingChangeGroups = previousGroups.Concat(currentGroups).Distinct().ToArray();

        Assert.Contains("User_7_WhatsApp", routingChangeGroups);
        Assert.DoesNotContain("User_7_WhatsApp", currentGroups);
        Assert.Contains("Branch_2_WhatsApp", currentGroups);
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task SeedCentralConversationAsync(
        ApplicationDbContext db,
        int? operationalBranchId,
        int? assignedUserId,
        int unreadCount = 0)
    {
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
                AttentionMode = WhatsAppAttentionMode.Ai,
                PhoneNumber = "3000000000"
            });
        await db.SaveChangesAsync();
    }

    private static WhatsAppController CreateController(ApplicationDbContext db, int userId = 7, int branchId = 1)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.Id).Returns(userId);
        currentUser.SetupGet(x => x.Role).Returns("Cashier");
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
            Mock.Of<IClock>(x => x.UtcNow == DateTime.UtcNow),
            Mock.Of<IWhatsAppCloudClient>(),
            notifications.Object,
            Mock.Of<IFirebaseGcsStorage>(),
            Options.Create(new FirebaseStorageOptions()),
            Options.Create(new WhatsAppCloudOptions()),
            Options.Create(new WhatsAppAiOrchestratorOptions()),
            Mock.Of<ILogger<WhatsAppController>>(),
            new WhatsAppAttentionService(),
            Mock.Of<IWhatsAppAiWorkQueue>(),
            Mock.Of<IWhatsAppAutomaticMessageSender>(),
            Mock.Of<IBranchBusinessHoursService>(),
            null!,
            null!);
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
