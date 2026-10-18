using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Constants;
using System.Reflection;

namespace SenorArroz.Tests;

public sealed class TransferOrderBranchTests
{
    [Fact]
    public void Transfer_endpoint_is_superadmin_only()
    {
        var action = typeof(OrdersController).GetMethod(nameof(OrdersController.TransferOrderBranch));
        Assert.Equal("Superadmin", action?.GetCustomAttribute<AuthorizeAttribute>()?.Roles);
    }

    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.OnTheWay)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.InPreparation)]
    [InlineData(OrderStatus.Taken)]
    public async Task Any_order_status_moves_without_reassigning_payments_products_or_delivery(OrderStatus status)
    {
        var tenant = new TestTenantContext();
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            currentTenant: tenant, tenantExecutionContext: tenant);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.Role).Returns(Roles.Superadmin);
        currentUser.SetupGet(u => u.TenantId).Returns(1);
        currentUser.SetupGet(u => u.BranchId).Returns(1);
        currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);
        var branch = new Mock<IBranchContext>();
        branch.SetupGet(b => b.EffectiveBranchId).Returns(1);

        var time = new DateTime(2026, 10, 18, 20, 0, 0, DateTimeKind.Utc);
        using (tenant.BeginSystemScope())
        {
            db.Branches.AddRange(
                new Branch { Id = 1, TenantId = 1, Name = "Origen" },
                new Branch { Id = 2, TenantId = 1, Name = "Destino" });
            db.Orders.Add(new Order
            {
                Id = 88, TenantId = 1, BranchId = 1, OriginBranchId = 1,
                TakenById = 11, Status = status, Type = OrderType.Delivery,
                DeliveryManId = 45, DeliveryRouteId = 321,
                Total = 40500, Subtotal = 40000, DeliveryFee = 500,
                CreatedAt = time, OrderDetails =
                [
                    new OrderDetail { TenantId = 1, ProductId = 777, Quantity = 2, UnitPrice = 20000 }
                ]
            });
            db.BankPayments.Add(new BankPayment
            {
                TenantId = 1, OrderId = 88, BankId = 93, Amount = 15000, IsVerified = true
            });
            db.KitchenVoiceEvents.Add(new KitchenVoiceEvent
            {
                TenantId = 1, BranchId = 1, OrderId = 88, Kind = "new"
            });
            await db.SaveChangesAsync();
        }

        var mutation = new Mock<IOrderMutationContext>();
        mutation.SetupGet(m => m.Expected).Returns((88, 1L));
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(time);
        var repository = new Mock<IOrderRepository>();
        repository.Setup(r => r.GetByIdWithFullDetailsAsync(88, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => db.Orders.AsNoTracking().Single(o => o.Id == 88));
        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<OrderDto>(It.IsAny<Order>()))
            .Returns((Order o) => new OrderDto { Id = o.Id, BranchId = o.BranchId, Status = o.Status });
        var notifications = new Mock<IOrderNotificationService>();
        notifications.Setup(n => n.NotifyOrderTransferred(88, 1, 2,
            status is OrderStatus.Taken or OrderStatus.InPreparation)).Returns(Task.CompletedTask);
        notifications.Setup(n => n.NotifyNewOrderToKitchen(It.IsAny<OrderDto>())).Returns(Task.CompletedTask);

        var handler = new TransferOrderBranchHandler(db, repository.Object, currentUser.Object,
            branch.Object, mutation.Object, notifications.Object, mapper.Object, clock.Object);

        var result = await handler.Handle(new TransferOrderBranchCommand { Id = 88, TargetBranchId = 2 }, default);
        var saved = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == 88);
        var detail = await db.OrderDetails.AsNoTracking().SingleAsync(d => d.OrderId == 88);
        var payment = await db.BankPayments.AsNoTracking().SingleAsync(p => p.OrderId == 88);

        Assert.Equal(2, result.BranchId);
        Assert.Equal(2, saved.BranchId);
        Assert.Equal(1, saved.OriginBranchId);
        Assert.Equal(status, saved.Status);
        Assert.Equal(321, saved.DeliveryRouteId);
        Assert.Equal(45, saved.DeliveryManId);
        Assert.Equal(40500, saved.Total);
        Assert.Equal(777, detail.ProductId);
        Assert.Equal(93, payment.BankId);
        Assert.True(payment.IsVerified);
        Assert.Equal(1, (await db.KitchenVoiceEvents.AsNoTracking()
            .SingleAsync(e => e.OrderId == 88)).BranchId);
        notifications.Verify(n => n.NotifyOrderTransferred(88, 1, 2,
            status is OrderStatus.Taken or OrderStatus.InPreparation), Times.Once);
        notifications.Verify(n => n.NotifyNewOrderToKitchen(It.IsAny<OrderDto>()), Times.Never());
    }

    [Fact]
    public async Task Transfer_requires_current_order_version()
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.Role).Returns(Roles.Superadmin);
        var mutation = new Mock<IOrderMutationContext>();
        var handler = new TransferOrderBranchHandler(
            Mock.Of<IApplicationDbContext>(), Mock.Of<IOrderRepository>(), user.Object,
            Mock.Of<IBranchContext>(), mutation.Object, Mock.Of<IOrderNotificationService>(),
            Mock.Of<IMapper>(), Mock.Of<IClock>());

        await Assert.ThrowsAsync<OrderConcurrencyException>(() =>
            handler.Handle(new TransferOrderBranchCommand { Id = 9, TargetBranchId = 2 }, default));
    }
}
