using AutoMapper;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Kitchen;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Constants;

namespace SenorArroz.Tests;

/// <summary>Cancelación de reservas: Admin puede cancelar en cualquier momento mientras no esté cancelado.</summary>
public class CancelOrderHandlerTests
{
    private sealed class TestCurrentUser(
        string role = Roles.Admin,
        int branchId = 1) : ICurrentUser
    {
        public int Id => 1;
        public string Role => role;
        public int BranchId => branchId;
        public bool IsAuthenticated => true;
    }

    private sealed class NullOrderNotifications : IOrderNotificationService
    {
        public int DeliveryModifiedCalls { get; private set; }
        public OrderDto? LastDeliveryModifiedOrder { get; private set; }
        public string? LastDeliveryModificationKind { get; private set; }
        public KitchenOrderModificationSummary? LastDeliveryKitchenChanges { get; private set; }

        public Task NotifyNewOrderToKitchen(OrderDto order) => Task.CompletedTask;

        public Task NotifyOrderReadyToDelivery(OrderDto order) => Task.CompletedTask;

        public Task NotifyReservationToKitchen(OrderDto order) => Task.CompletedTask;

        public Task NotifyOrderAssignedToDelivery(OrderDto order) => Task.CompletedTask;

        public Task NotifyOrderModifiedToKitchen(OrderDto order, string modificationKind, KitchenOrderModificationSummary? kitchenChanges = null) =>
            Task.CompletedTask;

        public Task NotifyOrderModifiedToDelivery(OrderDto order, string modificationKind, KitchenOrderModificationSummary? kitchenChanges = null)
        {
            DeliveryModifiedCalls++;
            LastDeliveryModifiedOrder = order;
            LastDeliveryModificationKind = modificationKind;
            LastDeliveryKitchenChanges = kitchenChanges;
            return Task.CompletedTask;
        }

        public Task NotifyOrderCancelledToKitchen(int branchId, int orderId, string? reasonPreview = null) => Task.CompletedTask;

        public Task NotifyDeliverymanLocation(
            int branchId,
            int deliverymanId,
            int? deliveryRouteId,
            double latitude,
            double longitude,
            DateTime recordedAt) => Task.CompletedTask;
    }

    private sealed class NullDeliveryRouteWorkflow : IDeliveryRouteWorkflowService
    {
        public Task OnOrderAssignedToDeliverymanAsync(Order order, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task OnOrderUnassignedAsync(int orderId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task OnOrderCancelledWhileRouteOpenAsync(int orderId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task TryCompleteInProgressRouteAsync(int orderId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task TryFinalizeRouteWhenAllTerminalAsync(
            int orderId,
            int? routeIdIfOrderUnlinked = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> DeliverymanHasPendingOrdersOnActiveRouteAsync(
            int deliverymanId,
            int branchId,
            CancellationToken cancellationToken = default,
            IReadOnlyCollection<int>? excludeOrderIds = null) =>
            Task.FromResult(false);

        public Task<int> ConsolidatePendingRoutesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class NullLoyaltyCycle : ILoyaltyCycleService
    {
        public Task ApplyLoyaltyPreviewToCustomerDtoAsync(CustomerDto dto, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task OnOrderDeliveredAsync(
            int orderId,
            int branchId,
            int? customerId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task OnOrderLeftDeliveredAsync(int orderId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static MapperConfiguration MapperCfg() =>
        new(cfg => { cfg.AddMaps(typeof(CancelOrderCommand).Assembly); }, NullLoggerFactory.Instance);

    private static CancelOrderHandler BuildHandler(
        IOrderRepository orderRepo,
        IClock clock,
        ICurrentUser? user = null,
        IOrderNotificationService? notifications = null,
        IAppPaymentRepository? appPaymentRepository = null,
        IExternalDeliveryStatusSyncService? externalDeliveryStatusSync = null)
    {
        var bank = new Mock<IBankPaymentRepository>();
        bank.Setup(r => r.GetByOrderIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BankPayment>());
        bank.Setup(r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        if (appPaymentRepository is null)
        {
            var apps = new Mock<IAppPaymentRepository>();
            apps.Setup(r => r.GetByOrderIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<AppPayment>());
            apps.Setup(r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            appPaymentRepository = apps.Object;
        }

        var deposits = new Mock<IReservationDepositRepository>();
        deposits.Setup(r => r.DeleteByOrderIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

        return new CancelOrderHandler(
            orderRepo,
            bank.Object,
            appPaymentRepository,
            deposits.Object,
            MapperCfg().CreateMapper(),
            user ?? new TestCurrentUser(),
            new NullLoyaltyCycle(),
            new NullDeliveryRouteWorkflow(),
            clock,
            notifications ?? new NullOrderNotifications(),
            externalDeliveryStatusSync);
    }

    private static Order ScheduledReservation(int id, DateTime createdAt, DateTime prepareAt, DateTime reservedFor) =>
        new()
        {
            Id = id,
            BranchId = 1,
            TakenById = 1,
            Type = OrderType.Reservation,
            Status = OrderStatus.Taken,
            CreatedAt = createdAt,
            PrepareAt = prepareAt,
            ReservedFor = reservedFor,
            Subtotal = 0,
            Total = 0,
            DiscountTotal = 0,
            StatusTimes = "{}",
        };

    [Fact]
    public void Cancel_endpoint_authorizes_cashier()
    {
        var method = typeof(OrdersController).GetMethod(nameof(OrdersController.CancelOrder))
            ?? throw new InvalidOperationException("CancelOrder method not found.");
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Admin,Superadmin,Cashier", authorize!.Roles);
    }

    [Fact]
    public async Task Cashier_can_cancel_order_from_own_branch()
    {
        var utcNow = new DateTime(2026, 8, 11, 15, 0, 0, DateTimeKind.Utc);
        var order = ScheduledReservation(43, utcNow, utcNow, utcNow);
        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetByIdAsync(43, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        orderRepo
            .Setup(r => r.CancelOrderAsync(43, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                order.Status = OrderStatus.Cancelled;
                order.CancelledReason = "Cliente canceló";
                return order;
            });
        var handler = BuildHandler(
            orderRepo.Object,
            new FakeClock(utcNow),
            new TestCurrentUser(Roles.Cashier));

        var result = await handler.Handle(
            new CancelOrderCommand
            {
                Id = 43,
                Cancellation = new CancelOrderDto { Reason = "Cliente canceló" },
            },
            CancellationToken.None);

        Assert.Equal(OrderStatus.Cancelled, result.Status);
        orderRepo.Verify(
            r => r.CancelOrderAsync(43, "Cliente canceló", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Scheduled_reservation_allows_cancel_even_when_dates_do_not_match_today()
    {
        var utcNow = new DateTime(2026, 4, 15, 15, 0, 0, DateTimeKind.Utc);
        var created = new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc);
        var prepareAt = new DateTime(2026, 4, 15, 20, 0, 0, DateTimeKind.Utc);
        var reservedFor = new DateTime(2026, 4, 15, 23, 0, 0, DateTimeKind.Utc);
        var order = ScheduledReservation(42, created, prepareAt, reservedFor);

        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetByIdAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        orderRepo
            .Setup(r => r.CancelOrderAsync(42, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                order.Status = OrderStatus.Cancelled;
                order.CancelledReason = "Motivo";
                return order;
            });

        var handler = BuildHandler(orderRepo.Object, new FakeClock(utcNow));

        await handler.Handle(
            new CancelOrderCommand
            {
                Id = 42,
                Cancellation = new CancelOrderDto { Reason = "Cliente avisó" },
            },
            CancellationToken.None);

        orderRepo.Verify(r => r.CancelOrderAsync(42, It.Is<string>(s => !string.IsNullOrWhiteSpace(s)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Scheduled_reservation_allows_cancel_when_none_of_dates_matches_today()
    {
        var utcNow = new DateTime(2026, 4, 15, 15, 0, 0, DateTimeKind.Utc);
        var created = new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc);
        var prepareAt = new DateTime(2026, 4, 14, 20, 0, 0, DateTimeKind.Utc);
        var reservedFor = new DateTime(2026, 4, 14, 23, 0, 0, DateTimeKind.Utc);
        var order = ScheduledReservation(7, created, prepareAt, reservedFor);

        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        orderRepo
            .Setup(r => r.CancelOrderAsync(7, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                order.Status = OrderStatus.Cancelled;
                order.CancelledReason = "Motivo";
                return order;
            });

        var handler = BuildHandler(orderRepo.Object, new FakeClock(utcNow));

        await handler.Handle(
            new CancelOrderCommand
            {
                Id = 7,
                Cancellation = new CancelOrderDto { Reason = "Cliente reprogramó" },
            },
            CancellationToken.None);

        orderRepo.Verify(r => r.CancelOrderAsync(7, It.Is<string>(s => !string.IsNullOrWhiteSpace(s)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancelling_on_the_way_order_notifies_delivery_route_update()
    {
        var utcNow = new DateTime(2026, 8, 10, 15, 0, 0, DateTimeKind.Utc);
        var order = ScheduledReservation(91, utcNow, utcNow, utcNow);
        order.Type = OrderType.Delivery;
        order.Status = OrderStatus.OnTheWay;
        order.DeliveryManId = 12;

        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetByIdAsync(91, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        orderRepo
            .Setup(r => r.CancelOrderAsync(91, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                order.Status = OrderStatus.Cancelled;
                order.CancelledReason = "Cancelado por la sucursal";
                return order;
            });
        var notifications = new NullOrderNotifications();
        var handler = BuildHandler(
            orderRepo.Object,
            new FakeClock(utcNow),
            notifications: notifications);

        var result = await handler.Handle(
            new CancelOrderCommand
            {
                Id = 91,
                Cancellation = new CancelOrderDto { Reason = "Cliente canceló" },
            },
            CancellationToken.None);

        Assert.Equal(OrderStatus.Cancelled, result.Status);
        Assert.Equal(1, notifications.DeliveryModifiedCalls);
        Assert.Equal(91, notifications.LastDeliveryModifiedOrder?.Id);
        Assert.Equal(OrderStatus.Cancelled, notifications.LastDeliveryModifiedOrder?.Status);
        Assert.Equal("status", notifications.LastDeliveryModificationKind);
        Assert.Null(notifications.LastDeliveryKitchenChanges);
    }

    [Fact]
    public async Task Rappi_order_syncs_cancellation_and_reverses_unsettled_app_payment()
    {
        var utcNow = new DateTime(2026, 8, 14, 17, 0, 0, DateTimeKind.Utc);
        var order = ScheduledReservation(101, utcNow, utcNow, utcNow);
        order.ExternalFulfillmentProvider = "rappi";
        order.ExternalOrderId = "rappi-101";
        var payment = new AppPayment { Id = 71, OrderId = order.Id, Amount = 25000 };

        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        orderRepo.Setup(r => r.CancelOrderAsync(order.Id, "Sin inventario", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                order.Status = OrderStatus.Cancelled;
                order.CancelledReason = "Sin inventario";
                return order;
            });

        var payments = new Mock<IAppPaymentRepository>();
        payments.Setup(r => r.GetByOrderIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([payment]);
        payments.Setup(r => r.UpdateAsync(payment, It.IsAny<CancellationToken>()))
            .ReturnsAsync(payment);

        var externalSync = new Mock<IExternalDeliveryStatusSyncService>();
        externalSync.Setup(x => x.SyncCancellationAsync(
                order.Id,
                "Sin inventario",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = BuildHandler(
            orderRepo.Object,
            new FakeClock(utcNow),
            appPaymentRepository: payments.Object,
            externalDeliveryStatusSync: externalSync.Object);

        var result = await handler.Handle(
            new CancelOrderCommand
            {
                Id = order.Id,
                Cancellation = new CancelOrderDto { Reason = "  Sin inventario  " },
            },
            CancellationToken.None);

        Assert.Equal(OrderStatus.Cancelled, result.Status);
        Assert.True(payment.IsReversed);
        Assert.Equal(utcNow, payment.ReversedAt);
        Assert.Equal("Cancelado desde Señor Arroz: Sin inventario", payment.ReversalReason);
        externalSync.VerifyAll();
        payments.Verify(r => r.UpdateAsync(payment, It.IsAny<CancellationToken>()), Times.Once);
        payments.Verify(r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Settled_Rappi_order_requires_reconciliation_before_cancellation()
    {
        var utcNow = new DateTime(2026, 8, 14, 17, 0, 0, DateTimeKind.Utc);
        var order = ScheduledReservation(102, utcNow, utcNow, utcNow);
        order.ExternalFulfillmentProvider = "rappi";
        var payment = new AppPayment
        {
            Id = 72,
            OrderId = order.Id,
            Amount = 25000,
            IsSetted = true,
        };

        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetByIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        var payments = new Mock<IAppPaymentRepository>();
        payments.Setup(r => r.GetByOrderIdAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([payment]);
        var externalSync = new Mock<IExternalDeliveryStatusSyncService>();
        var handler = BuildHandler(
            orderRepo.Object,
            new FakeClock(utcNow),
            appPaymentRepository: payments.Object,
            externalDeliveryStatusSync: externalSync.Object);

        var error = await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(
            new CancelOrderCommand
            {
                Id = order.Id,
                Cancellation = new CancelOrderDto { Reason = "Sin inventario" },
            },
            CancellationToken.None));

        Assert.Contains("requiere conciliación", error.Message);
        externalSync.Verify(x => x.SyncCancellationAsync(
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        orderRepo.Verify(r => r.CancelOrderAsync(
            It.IsAny<int>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
