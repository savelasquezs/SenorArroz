using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Kitchen;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Constants;

namespace SenorArroz.Tests;

/// <summary>
/// Regresión: pasar de reserva a domicilio/local no debe borrar reserved_for ni prepare_at.
/// </summary>
public class UpdateOrderHandlerSchedulePreserveTests
{
    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int Id => 1;
        public string Role => Roles.Cashier;
        public int BranchId => 1;
        public bool IsAuthenticated => true;
    }

    private sealed class NullNotifications : IOrderNotificationService
    {
        public Task NotifyNewOrderToKitchen(OrderDto order) => Task.CompletedTask;
        public Task NotifyOrderReadyToDelivery(OrderDto order) => Task.CompletedTask;
        public Task NotifyReservationToKitchen(OrderDto order) => Task.CompletedTask;
        public Task NotifyOrderAssignedToDelivery(OrderDto order) => Task.CompletedTask;
        public Task NotifyOrderModifiedToKitchen(OrderDto order, string modificationKind, KitchenOrderModificationSummary? kitchenChanges = null) => Task.CompletedTask;
        public Task NotifyOrderModifiedToDelivery(OrderDto order, string modificationKind, KitchenOrderModificationSummary? kitchenChanges = null) => Task.CompletedTask;
        public Task NotifyOrderCancelledToKitchen(int branchId, int orderId, string? reasonPreview = null) => Task.CompletedTask;
        public Task NotifyDeliverymanLocation(int branchId, int deliverymanId, int deliveryRouteId, double latitude, double longitude, DateTime recordedAt) => Task.CompletedTask;
    }

    private sealed class CaptureNotifications : IOrderNotificationService
    {
        public int KitchenModifiedCalls { get; private set; }
        public string? LastModificationKind { get; private set; }
        public KitchenOrderModificationSummary? LastKitchenChanges { get; private set; }

        public Task NotifyNewOrderToKitchen(OrderDto order) => Task.CompletedTask;
        public Task NotifyOrderReadyToDelivery(OrderDto order) => Task.CompletedTask;
        public Task NotifyReservationToKitchen(OrderDto order) => Task.CompletedTask;
        public Task NotifyOrderAssignedToDelivery(OrderDto order) => Task.CompletedTask;
        public Task NotifyOrderCancelledToKitchen(int branchId, int orderId, string? reasonPreview = null) => Task.CompletedTask;
        public Task NotifyDeliverymanLocation(int branchId, int deliverymanId, int deliveryRouteId, double latitude, double longitude, DateTime recordedAt) => Task.CompletedTask;
        public Task NotifyOrderModifiedToDelivery(OrderDto order, string modificationKind, KitchenOrderModificationSummary? kitchenChanges = null) => Task.CompletedTask;

        public Task NotifyOrderModifiedToKitchen(OrderDto order, string modificationKind, KitchenOrderModificationSummary? kitchenChanges = null)
        {
            KitchenModifiedCalls++;
            LastModificationKind = modificationKind;
            LastKitchenChanges = kitchenChanges;
            return Task.CompletedTask;
        }
    }

    private static UpdateOrderHandler BuildHandler(
        IOrderRepository repo,
        IAddressRepository addressRepo,
        DateTime utcNow,
        IOrderNotificationService? notifications = null,
        Mock<IBankPaymentRepository>? bankPaymentRepoMock = null,
        Mock<IReservationDepositRepository>? reservationDepositRepoMock = null)
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.AddMaps(typeof(UpdateOrderCommand).Assembly);
        }, NullLoggerFactory.Instance).CreateMapper();
        var clock = new FixedClock(utcNow);
        var bankPaymentRepo = bankPaymentRepoMock ?? new Mock<IBankPaymentRepository>();
        var reservationDepositRepo = reservationDepositRepoMock ?? new Mock<IReservationDepositRepository>();
        if (bankPaymentRepoMock is null)
        {
            bankPaymentRepo.Setup(r => r.GetByOrderIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<BankPayment>());
        }
        if (reservationDepositRepoMock is null)
        {
            reservationDepositRepo.Setup(r => r.GetByOrderIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ReservationDeposit>());
        }
        return new UpdateOrderHandler(
            repo,
            addressRepo,
            bankPaymentRepo.Object,
            reservationDepositRepo.Object,
            mapper,
            new TestCurrentUser(),
            new OrderBusinessRulesService(clock),
            notifications ?? new NullNotifications(),
            clock);
    }

    [Fact]
    public async Task Type_change_reservation_to_delivery_without_schedule_in_dto_preserves_times()
    {
        var utc = new DateTime(2026, 4, 19, 15, 0, 0, DateTimeKind.Utc);
        var reservedFor = utc.AddDays(1);
        var prepareAt = reservedFor.AddMinutes(-45);

        var order = new Order
        {
            Id = 42,
            BranchId = 1,
            TakenById = 1,
            Type = OrderType.Reservation,
            Status = OrderStatus.Taken,
            ReservedFor = reservedFor,
            PrepareAt = prepareAt,
            AddressId = 5,
            GuestName = "Cliente",
            CreatedAt = utc,
            OrderDetails = new List<OrderDetail>
            {
                new()
                {
                    Id = 1,
                    OrderId = 42,
                    ProductId = 1,
                    Quantity = 1,
                    UnitPrice = 10_000,
                    Discount = 0,
                    Subtotal = 10_000,
                },
            },
        };

        var repo = new Mock<IOrderRepository>();
        repo.Setup(r => r.GetByIdWithDetailsAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        repo.Setup(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Returns<Order, CancellationToken>((o, _) => Task.FromResult(o));

        var addressRepo = new Mock<IAddressRepository>();

        var handler = BuildHandler(repo.Object, addressRepo.Object, utc);
        await handler.Handle(
            new UpdateOrderCommand
            {
                Id = 42,
                Order = new UpdateOrderDto { Type = OrderType.Delivery },
            },
            CancellationToken.None);

        Assert.Equal(OrderType.Delivery, order.Type);
        Assert.Equal(reservedFor, order.ReservedFor);
        Assert.Equal(prepareAt, order.PrepareAt);
    }

    [Fact]
    public async Task Reserved_for_sent_without_prepare_at_recalculates_prepare_at_minus_one_hour()
    {
        var utc = new DateTime(2026, 4, 19, 15, 0, 0, DateTimeKind.Utc);
        var newReserved = utc.AddDays(2);
        var oldReserved = utc.AddDays(1);
        var oldPrepare = oldReserved.AddMinutes(-50);

        var order = new Order
        {
            Id = 43,
            BranchId = 1,
            TakenById = 1,
            Type = OrderType.Reservation,
            Status = OrderStatus.Taken,
            ReservedFor = oldReserved,
            PrepareAt = oldPrepare,
            CreatedAt = utc,
            OrderDetails = new List<OrderDetail>
            {
                new()
                {
                    Id = 1,
                    OrderId = 43,
                    ProductId = 1,
                    Quantity = 1,
                    UnitPrice = 10_000,
                    Discount = 0,
                    Subtotal = 10_000,
                },
            },
        };

        var repo = new Mock<IOrderRepository>();
        repo.Setup(r => r.GetByIdWithDetailsAsync(43, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        repo.Setup(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Returns<Order, CancellationToken>((o, _) => Task.FromResult(o));

        var addressRepo = new Mock<IAddressRepository>();

        var handler = BuildHandler(repo.Object, addressRepo.Object, utc);
        await handler.Handle(
            new UpdateOrderCommand
            {
                Id = 43,
                Order = new UpdateOrderDto { ReservedFor = newReserved },
            },
            CancellationToken.None);

        Assert.Equal(newReserved, order.ReservedFor);
        Assert.Equal(newReserved.AddHours(-1), order.PrepareAt);
    }

    [Fact]
    public async Task Schedule_change_to_future_reservation_notifies_kitchen_with_reservation_kind()
    {
        var utc = new DateTime(2026, 4, 19, 15, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            Id = 44,
            BranchId = 1,
            TakenById = 1,
            Type = OrderType.Onsite,
            Status = OrderStatus.Taken,
            CreatedAt = utc,
            OrderDetails = new List<OrderDetail>
            {
                new()
                {
                    Id = 1,
                    OrderId = 44,
                    ProductId = 1,
                    Quantity = 1,
                    UnitPrice = 10_000,
                    Discount = 0,
                    Subtotal = 10_000,
                },
            },
        };

        var repo = new Mock<IOrderRepository>();
        repo.Setup(r => r.GetByIdWithDetailsAsync(44, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        repo.Setup(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Returns<Order, CancellationToken>((o, _) => Task.FromResult(o));

        var notifications = new CaptureNotifications();
        var handler = BuildHandler(repo.Object, Mock.Of<IAddressRepository>(), utc, notifications);

        await handler.Handle(
            new UpdateOrderCommand
            {
                Id = 44,
                Order = new UpdateOrderDto { ReservedFor = utc.AddHours(3) },
            },
            CancellationToken.None);

        Assert.Equal(1, notifications.KitchenModifiedCalls);
        Assert.Equal("schedule", notifications.LastModificationKind);
        Assert.Equal("reservation", notifications.LastKitchenChanges?.ScheduleChangeKind);
    }

    [Fact]
    public async Task Schedule_change_to_prepare_now_notifies_kitchen_with_prepare_now_kind()
    {
        var utc = new DateTime(2026, 4, 19, 15, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            Id = 45,
            BranchId = 1,
            TakenById = 1,
            Type = OrderType.Reservation,
            Status = OrderStatus.Taken,
            ReservedFor = utc.AddHours(4),
            PrepareAt = utc.AddHours(3),
            CreatedAt = utc,
            OrderDetails = new List<OrderDetail>
            {
                new()
                {
                    Id = 1,
                    OrderId = 45,
                    ProductId = 1,
                    Quantity = 1,
                    UnitPrice = 10_000,
                    Discount = 0,
                    Subtotal = 10_000,
                },
            },
        };

        var repo = new Mock<IOrderRepository>();
        repo.Setup(r => r.GetByIdWithDetailsAsync(45, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        repo.Setup(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Returns<Order, CancellationToken>((o, _) => Task.FromResult(o));

        var notifications = new CaptureNotifications();
        var handler = BuildHandler(repo.Object, Mock.Of<IAddressRepository>(), utc, notifications);

        await handler.Handle(
            new UpdateOrderCommand
            {
                Id = 45,
                Order = new UpdateOrderDto
                {
                    Type = OrderType.Onsite,
                    PrepareAt = utc,
                    ReservedFor = null,
                },
            },
            CancellationToken.None);

        Assert.Equal(1, notifications.KitchenModifiedCalls);
        Assert.Equal("schedule", notifications.LastModificationKind);
        Assert.Equal("prepare_now", notifications.LastKitchenChanges?.ScheduleChangeKind);
    }

    [Fact]
    public async Task Prepare_now_from_reservation_promotes_bank_reservation_deposit_to_bank_payment()
    {
        var utc = new DateTime(2026, 4, 19, 15, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            Id = 46,
            BranchId = 1,
            TakenById = 1,
            Type = OrderType.Reservation,
            Status = OrderStatus.Taken,
            ReservedFor = utc.AddHours(4),
            PrepareAt = utc.AddHours(3),
            CreatedAt = utc,
            OrderDetails = new List<OrderDetail>
            {
                new()
                {
                    Id = 1,
                    OrderId = 46,
                    ProductId = 1,
                    Quantity = 1,
                    UnitPrice = 10_000,
                    Discount = 0,
                    Subtotal = 10_000,
                },
            },
        };

        var repo = new Mock<IOrderRepository>();
        repo.Setup(r => r.GetByIdWithDetailsAsync(46, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        repo.Setup(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Returns<Order, CancellationToken>((o, _) => Task.FromResult(o));

        var bankPaymentRepo = new Mock<IBankPaymentRepository>();
        bankPaymentRepo.Setup(r => r.GetByOrderIdAsync(46, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BankPayment>());
        bankPaymentRepo.Setup(r => r.CreateAsync(It.IsAny<BankPayment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BankPayment bp, CancellationToken _) => bp);

        var depositRepo = new Mock<IReservationDepositRepository>();
        depositRepo.Setup(r => r.GetByOrderIdAsync(46, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReservationDeposit>
            {
                new()
                {
                    Id = 100,
                    OrderId = 46,
                    BranchId = 1,
                    Amount = 200000m,
                    IsEffective = false,
                    BankId = 3,
                    AppId = null,
                    ReceivedAt = utc.AddHours(-2),
                    ReceivedById = 1,
                },
            });

        var handler = BuildHandler(
            repo.Object,
            Mock.Of<IAddressRepository>(),
            utc,
            notifications: new CaptureNotifications(),
            bankPaymentRepoMock: bankPaymentRepo,
            reservationDepositRepoMock: depositRepo);

        await handler.Handle(
            new UpdateOrderCommand
            {
                Id = 46,
                Order = new UpdateOrderDto
                {
                    Type = OrderType.Onsite,
                    PrepareAt = utc,
                    ReservedFor = null,
                },
            },
            CancellationToken.None);

        bankPaymentRepo.Verify(r => r.CreateAsync(
            It.Is<BankPayment>(bp =>
                bp.OrderId == 46
                && bp.BankId == 3
                && bp.Amount == 200000m
                && bp.SourceReservationDepositId == 100),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
