using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Constants;

namespace SenorArroz.Tests;

public class SelfAssignOrdersHandlerTests
{
    [Fact]
    public void Assignment_contract_does_not_expose_a_password()
    {
        Assert.Null(typeof(SelfAssignOrdersDto).GetProperty("Password"));
        Assert.Null(typeof(SelfAssignOrdersCommand).GetProperty("Password"));
    }

    [Fact]
    public async Task Authenticated_deliveryman_assigns_ready_order_even_when_notification_fails()
    {
        var order = new Order
        {
            Id = 11,
            BranchId = 1,
            TakenById = 1,
            Type = OrderType.Delivery,
            Status = OrderStatus.Ready,
            StatusTimes = "{}",
        };
        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetByIdAsync(11, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        orders.Setup(x => x.AssignDeliveryManAsync(11, 24, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                order.DeliveryManId = 24;
                return order;
            });
        orders.Setup(x => x.ChangeStatusAsync(11, OrderStatus.OnTheWay, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                order.Status = OrderStatus.OnTheWay;
                return order;
            });

        var mapper = new Mock<IMapper>();
        mapper.Setup(x => x.Map<OrderDto>(It.IsAny<Order>()))
            .Returns((Order source) => new OrderDto { Id = source.Id, Status = source.Status });
        var notifications = new Mock<IOrderNotificationService>();
        notifications.Setup(x => x.NotifyOrderAssignedToDelivery(It.IsAny<OrderDto>()))
            .ThrowsAsync(new InvalidOperationException("Notification temporarily unavailable"));
        var printing = new Mock<IPrintQueueService>();
        printing.Setup(x => x.EnqueueAsync(
                1, PrintJobKind.Delivery, It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrintJob { Id = 1, BranchId = 1, Kind = PrintJobKind.Delivery, Status = PrintJobStatus.Pending });

        var routes = new Mock<IDeliveryRouteWorkflowService>();
        routes.Setup(x => x.DeliverymanHasPendingOrdersOnActiveRouteAsync(
                24, 1, It.IsAny<CancellationToken>(), It.IsAny<IReadOnlyCollection<int>>()))
            .ReturnsAsync(false);
        routes.Setup(x => x.OnOrderAssignedToDeliverymanAsync(
                It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new SelfAssignOrdersHandler(
            orders.Object,
            mapper.Object,
            CurrentDeliveryman(),
            notifications.Object,
            routes.Object,
            printing.Object,
            NullLogger<SelfAssignOrdersHandler>.Instance);

        var result = await handler.Handle(new SelfAssignOrdersCommand { OrderIds = [11] }, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(11, result[0].Id);
        Assert.Equal(24, order.DeliveryManId);
        Assert.Equal(OrderStatus.OnTheWay, order.Status);
        notifications.Verify(x => x.NotifyOrderAssignedToDelivery(It.IsAny<OrderDto>()), Times.Once);
        printing.Verify(x => x.EnqueueAsync(
            1, PrintJobKind.Delivery, It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Assignment_still_rejects_an_order_that_is_not_ready()
    {
        var order = new Order
        {
            Id = 11,
            BranchId = 1,
            TakenById = 1,
            Type = OrderType.Delivery,
            Status = OrderStatus.OnTheWay,
        };
        var orders = new Mock<IOrderRepository>();
        orders.Setup(x => x.GetByIdAsync(11, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        var handler = new SelfAssignOrdersHandler(
            orders.Object,
            Mock.Of<IMapper>(),
            CurrentDeliveryman(),
            Mock.Of<IOrderNotificationService>(),
            Mock.Of<IDeliveryRouteWorkflowService>(),
            Mock.Of<IPrintQueueService>(),
            NullLogger<SelfAssignOrdersHandler>.Instance);

        await Assert.ThrowsAsync<RoutingPlanStaleException>(() =>
            handler.Handle(new SelfAssignOrdersCommand { OrderIds = [11] }, CancellationToken.None));

        orders.Verify(x => x.AssignDeliveryManAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ICurrentUser CurrentDeliveryman()
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.Id).Returns(24);
        user.SetupGet(x => x.Role).Returns(Roles.Deliveryman);
        user.SetupGet(x => x.BranchId).Returns(1);
        user.SetupGet(x => x.IsAuthenticated).Returns(true);
        return user.Object;
    }
}
