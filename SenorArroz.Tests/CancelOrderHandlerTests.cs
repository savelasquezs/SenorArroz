using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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

/// <summary>Cancelación de reserva programada: ventana temporal alineada a creación / prepareAt / reservedFor en calendario Colombia.</summary>
public class CancelOrderHandlerTests
{
    private sealed class TestCurrentUser : ICurrentUser
    {
        public int Id => 1;
        public string Role => Roles.Admin;
        public int BranchId => 1;
        public bool IsAuthenticated => true;
    }

    private sealed class NullOrderNotifications : IOrderNotificationService
    {
        public Task NotifyNewOrderToKitchen(OrderDto order) => Task.CompletedTask;

        public Task NotifyOrderReadyToDelivery(OrderDto order) => Task.CompletedTask;

        public Task NotifyReservationToKitchen(OrderDto order) => Task.CompletedTask;

        public Task NotifyOrderAssignedToDelivery(OrderDto order) => Task.CompletedTask;

        public Task NotifyOrderModifiedToKitchen(OrderDto order, string modificationKind, KitchenOrderModificationSummary? kitchenChanges = null) =>
            Task.CompletedTask;

        public Task NotifyOrderModifiedToDelivery(OrderDto order, string modificationKind, KitchenOrderModificationSummary? kitchenChanges = null) =>
            Task.CompletedTask;

        public Task NotifyOrderCancelledToKitchen(int branchId, int orderId, string? reasonPreview = null) => Task.CompletedTask;

        public Task NotifyDeliverymanLocation(
            int branchId,
            int deliverymanId,
            int deliveryRouteId,
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
        ICurrentUser? user = null)
    {
        var bank = new Mock<IBankPaymentRepository>();
        bank.Setup(r => r.GetByOrderIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BankPayment>());
        bank.Setup(r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var apps = new Mock<IAppPaymentRepository>();
        apps.Setup(r => r.GetByOrderIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AppPayment>());
        apps.Setup(r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var deposits = new Mock<IReservationDepositRepository>();
        deposits.Setup(r => r.DeleteByOrderIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

        return new CancelOrderHandler(
            orderRepo,
            bank.Object,
            apps.Object,
            deposits.Object,
            MapperCfg().CreateMapper(),
            user ?? new TestCurrentUser(),
            new NullLoyaltyCycle(),
            new NullDeliveryRouteWorkflow(),
            clock,
            new NullOrderNotifications());
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
    public async Task Scheduled_reservation_allows_cancel_on_prepare_day_when_created_earlier()
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
    public async Task Scheduled_reservation_blocks_cancel_when_none_of_dates_is_colombia_today()
    {
        var utcNow = new DateTime(2026, 4, 15, 15, 0, 0, DateTimeKind.Utc);
        var created = new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc);
        var prepareAt = new DateTime(2026, 4, 14, 20, 0, 0, DateTimeKind.Utc);
        var reservedFor = new DateTime(2026, 4, 14, 23, 0, 0, DateTimeKind.Utc);
        var order = ScheduledReservation(7, created, prepareAt, reservedFor);

        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(order);

        var handler = BuildHandler(orderRepo.Object, new FakeClock(utcNow));

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            handler.Handle(
                new CancelOrderCommand { Id = 7, Cancellation = new CancelOrderDto { Reason = "x" } },
                CancellationToken.None));

        Assert.Contains("prepareAt", ex.Message);
        Assert.Contains("reservedFor", ex.Message);

        orderRepo.Verify(
            r => r.CancelOrderAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
