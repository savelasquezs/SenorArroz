using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using AutoMapper;
using SenorArroz.API.Controllers;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
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

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task SeedCentralConversationAsync(ApplicationDbContext db, int? operationalBranchId, int? assignedUserId)
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
                AttentionMode = WhatsAppAttentionMode.Ai,
                PhoneNumber = "3000000000"
            });
        await db.SaveChangesAsync();
    }

    private static WhatsAppController CreateController(ApplicationDbContext db)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.Id).Returns(7);
        currentUser.SetupGet(x => x.Role).Returns("Cashier");
        currentUser.SetupGet(x => x.BranchId).Returns(1);
        var branchContext = new Mock<IBranchContext>();
        branchContext.Setup(x => x.RequireBranch(It.IsAny<int?>())).Returns(1);
        var notifications = new Mock<IWhatsAppNotificationService>();
        notifications.Setup(x => x.NotifyAttentionChangedAsync(It.IsAny<int>(), It.IsAny<WhatsAppConversationDto>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        notifications.Setup(x => x.NotifyConversationRoutingChangedAsync(It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<WhatsAppConversationDto>(), It.IsAny<CancellationToken>()))
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
}
