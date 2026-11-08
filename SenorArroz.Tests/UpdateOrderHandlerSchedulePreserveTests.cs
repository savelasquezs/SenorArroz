using AutoMapper;
using SenorArroz.Application.Common.Helpers;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Infrastructure.Data;
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

    private sealed class TestCurrentUser(string role = Roles.Cashier) : ICurrentUser
    {
        public int Id => 1;
        public string Role => role;
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
        public Task NotifyDeliverymanLocation(int branchId, int deliverymanId, int? deliveryRouteId, double latitude, double longitude, DateTime recordedAt) => Task.CompletedTask;
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
        public Task NotifyDeliverymanLocation(int branchId, int deliverymanId, int? deliveryRouteId, double latitude, double longitude, DateTime recordedAt) => Task.CompletedTask;
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
        Mock<IReservationDepositRepository>? reservationDepositRepoMock = null,
        IApplicationDbContext? db = null,
        string role = Roles.Cashier)
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
            db ?? new Mock<IApplicationDbContext>().Object,
            addressRepo,
            bankPaymentRepo.Object,
            reservationDepositRepo.Object,
            mapper,
            new TestCurrentUser(role),
            new OrderBusinessRulesService(clock),
            notifications ?? new NullNotifications(),
            clock);
    }

    [Theory]
    [InlineData(OrderStatus.Taken, 0)]
    [InlineData(OrderStatus.InPreparation, 3500)]
    [InlineData(OrderStatus.Ready, 5000)]
    [InlineData(OrderStatus.OnTheWay, 6000)]
    public async Task Cashier_updates_delivery_fee_before_delivery_and_recalculates_total(OrderStatus status, int fee)
    {
        var utc = new DateTime(2026, 11, 8, 15, 0, 0, DateTimeKind.Utc);
        var order = FeeOrder(utc, status);
        var repo = OrderRepository(order);
        var handler = BuildHandler(repo.Object, Mock.Of<IAddressRepository>(), utc);

        await handler.Handle(new UpdateOrderCommand { Id = order.Id, Order = new() { DeliveryFee = fee } }, default);

        Assert.Equal(fee, order.DeliveryFee);
        Assert.Equal(10000 + fee, order.Total);
        repo.Verify(x => x.UpdateAsync(order, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.AwaitingPayment)]
    public async Task Cashier_cannot_change_fee_after_delivery_cancellation_or_during_online_payment(OrderStatus status)
    {
        var utc = new DateTime(2026, 11, 8, 15, 0, 0, DateTimeKind.Utc);
        var order = FeeOrder(utc, status);
        var repo = OrderRepository(order);
        var handler = BuildHandler(repo.Object, Mock.Of<IAddressRepository>(), utc);

        await Assert.ThrowsAsync<SenorArroz.Domain.Exceptions.BusinessException>(() => handler.Handle(
            new UpdateOrderCommand { Id = order.Id, Order = new() { DeliveryFee = 4000 } }, default));

        Assert.Equal(5000, order.DeliveryFee);
        repo.Verify(x => x.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Negative_fee_is_rejected_before_changing_the_order()
    {
        var utc = new DateTime(2026, 11, 8, 15, 0, 0, DateTimeKind.Utc);
        var order = FeeOrder(utc, OrderStatus.Ready);
        var repo = OrderRepository(order);
        var handler = BuildHandler(repo.Object, Mock.Of<IAddressRepository>(), utc);

        await Assert.ThrowsAsync<SenorArroz.Domain.Exceptions.BusinessException>(() => handler.Handle(
            new UpdateOrderCommand { Id = order.Id, Order = new() { DeliveryFee = -1 } }, default));

        Assert.Equal(5000, order.DeliveryFee);
        repo.Verify(x => x.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cashier_corrects_delivered_order_with_unchanged_delivery_fee(bool changeProducts)
    {
        var utc = new DateTime(2026, 11, 8, 15, 0, 0, DateTimeKind.Utc);
        var order = FeeOrder(utc, OrderStatus.Delivered);
        var repo = OrderRepository(order);
        var handler = BuildHandler(repo.Object, Mock.Of<IAddressRepository>(), utc);
        var update = new UpdateOrderDto { DeliveryFee = 5000, Notes = "Correccion del pedido" };
        if (changeProducts)
            update.OrderDetails = [new() { Id = 1, ProductId = 10, Quantity = 2, UnitPrice = 10000 }];

        await handler.Handle(new UpdateOrderCommand { Id = order.Id, Order = update }, default);

        Assert.Equal(5000, order.DeliveryFee);
        Assert.Equal("Correccion del pedido", order.Notes);
        Assert.Equal(changeProducts ? 25000 : 15000, order.Total);
        repo.Verify(x => x.UpdateAsync(order, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(Roles.Admin, true, OrderStatus.Taken)]
    [InlineData(Roles.Superadmin, true, OrderStatus.Taken)]
    [InlineData(Roles.Admin, true, OrderStatus.Delivered)]
    [InlineData(Roles.Superadmin, true, OrderStatus.Delivered)]
    [InlineData(Roles.Cashier, false, OrderStatus.Taken)]
    public async Task An_order_without_a_gift_can_receive_an_authorized_gift_from_the_shared_catalog(string role, bool allowed, OrderStatus status)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
        var category = new ProductCategory { Id = 80, BranchId = 2, Name = "Regalos" };
        db.Products.Add(new Product { Id = 90, Category = category, CategoryId = 80, Name = "Papas", Price = 2000, Active = true });
        db.Users.Add(new User { Id = 1, Name = "Admin", BranchId = 1 });
        await db.SaveChangesAsync();
        var utc = new DateTime(2026, 11, 8, 15, 0, 0, DateTimeKind.Utc);
        var order = FeeOrder(utc, status);
        var repo = OrderRepository(order);
        var handler = BuildHandler(repo.Object, Mock.Of<IAddressRepository>(), utc, db: db, role: role);
        var command = new UpdateOrderCommand { Id = order.Id, Order = new()
        {
            AppliedBenefitType = OrderBenefitType.Manual, AppliedBenefitRewardType = LoyaltyRewardType.GiftProduct,
            AppliedBenefitLabel = "Regalo manual: Papas", ManualBenefitGiftProductId = 90,
            ManualBenefitReason = "Cortesia",
            OrderDetails = [new() { Id = 1, ProductId = 10, Quantity = 1, UnitPrice = 10000 },
                new() { ProductId = 90, Quantity = 1, UnitPrice = 0 }],
        }};
        if (!allowed)
        {
            await Assert.ThrowsAsync<SenorArroz.Domain.Exceptions.BusinessException>(() => handler.Handle(command, default));
            Assert.Single(order.OrderDetails);
            repo.Verify(x => x.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
            return;
        }

        await handler.Handle(command, default);

        Assert.Equal(15000, order.Total);
        var gift = Assert.Single(order.OrderDetails, line => line.ProductId == 90);
        Assert.Equal(0, gift.UnitPrice);
        Assert.Equal(1, gift.Quantity);
        Assert.Equal(OrderBenefitType.Manual, order.AppliedBenefitType);
        Assert.Equal(90, order.ManualBenefitGiftProductId);
        Assert.Equal(1, order.ManualBenefitGrantedByUserId);
        Assert.Equal("Cortesia", order.ManualBenefitReason);
        Assert.Equal(utc, order.ManualBenefitGrantedAt);
    }

    [Theory]
    [InlineData(true, " Regalos ", true)]
    [InlineData(false, "Regalos", false)]
    [InlineData(true, "Bebidas", false)]
    public async Task Gift_eligibility_requires_an_active_gift_product_in_the_current_tenant(bool active, string categoryName, bool valid)
    {
        var tenant = new TestTenantContext(2);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, currentTenant: tenant, tenantExecutionContext: tenant);
        db.Products.Add(new Product { Id = 90, Active = active, Name = "Papas",
            Category = new ProductCategory { Id = 80, BranchId = 2, Name = categoryName } });
        await db.SaveChangesAsync();
        Assert.Equal(valid, await OrderGiftEligibility.IsValidProductAsync(db, 90, default));
        using (tenant.BeginTenantScope(1))
            Assert.False(await OrderGiftEligibility.IsValidProductAsync(db, 90, default));
    }

    private static Order FeeOrder(DateTime utc, OrderStatus status) => new()
    {
        Id = 249, BranchId = 1, TenantId = 1, TakenById = 1, Type = OrderType.Delivery,
        Status = status, CreatedAt = utc, DeliveryFee = 5000, Total = 15000,
        OrderDetails = [new() { Id = 1, ProductId = 10, Quantity = 1, UnitPrice = 10000, Subtotal = 10000 }],
    };

    private static Mock<IOrderRepository> OrderRepository(Order order)
    {
        var repo = new Mock<IOrderRepository>();
        repo.Setup(x => x.GetByIdWithDetailsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        repo.Setup(x => x.GetByIdWithFullDetailsAsync(order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        repo.Setup(x => x.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>())).ReturnsAsync((Order value, CancellationToken _) => value);
        return repo;
    }

    [Theory]
    [InlineData(OrderType.Onsite)]
    [InlineData(OrderType.Reservation)]
    public async Task An_onsite_pickup_cannot_have_its_name_cleared_or_be_converted_without_a_name(OrderType previousType)
    {
        var utc = new DateTime(2026, 4, 19, 15, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            Id = 147,
            BranchId = 1,
            OriginBranchId = 2, // Pedido tomado desde otra sucursal.
            TakenById = 1,
            Type = previousType,
            Status = OrderStatus.Taken,
            GuestName = previousType == OrderType.Onsite ? "Carlos" : null,
            CreatedAt = utc,
        };
        var repo = new Mock<IOrderRepository>();
        repo.Setup(r => r.GetByIdWithDetailsAsync(147, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        var handler = BuildHandler(repo.Object, Mock.Of<IAddressRepository>(), utc);
        var update = new UpdateOrderDto { GuestName = "   " };
        if (previousType != OrderType.Onsite)
            update.Type = OrderType.Onsite;

        var exception = await Assert.ThrowsAsync<SenorArroz.Domain.Exceptions.BusinessException>(() =>
            handler.Handle(new UpdateOrderCommand { Id = 147, Order = update }, CancellationToken.None));

        Assert.Contains("nombre de quien recoge", exception.Message, StringComparison.OrdinalIgnoreCase);
        repo.Verify(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(OrderStatus.Taken, null, "Sin cebolla", true)]
    [InlineData(OrderStatus.InPreparation, "Sin cebolla", "Salsa aparte", true)]
    [InlineData(OrderStatus.Taken, "Sin cebolla", "", true)]
    [InlineData(OrderStatus.Ready, "Sin cebolla", "Salsa aparte", false)]
    public async Task Product_note_only_updates_publish_the_correct_kitchen_change(
        OrderStatus status, string? previousNotes, string notes, bool shouldNotify)
    {
        var utc = new DateTime(2026, 10, 22, 18, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            Id = 148, BranchId = 1, TakenById = 1, Type = OrderType.Delivery,
            Status = status, CreatedAt = utc, Notes = "Nota general sin cambios",
            OrderDetails = [new()
            {
                Id = 1, OrderId = 148, ProductId = 10, Quantity = 2,
                UnitPrice = 10000, Subtotal = 20000, Notes = previousNotes,
                Product = new Product { Name = "Arroz paisa familiar" },
            }],
        };
        var repo = new Mock<IOrderRepository>();
        repo.Setup(r => r.GetByIdWithDetailsAsync(148, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        repo.Setup(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Returns<Order, CancellationToken>((o, _) => Task.FromResult(o));
        var notifications = new CaptureNotifications();
        var handler = BuildHandler(repo.Object, Mock.Of<IAddressRepository>(), utc, notifications);

        await handler.Handle(new UpdateOrderCommand
        {
            Id = 148, Order = new UpdateOrderDto { OrderDetails = [new()
            {
                Id = 1, ProductId = 10, Quantity = 2, UnitPrice = 10000, Notes = notes,
            }] },
        }, CancellationToken.None);

        Assert.Equal(notes, order.OrderDetails.Single().Notes);
        Assert.Equal(20000, order.OrderDetails.Single().Subtotal);
        Assert.Equal("Nota general sin cambios", order.Notes);
        Assert.Equal(shouldNotify ? 1 : 0, notifications.KitchenModifiedCalls);
        if (shouldNotify)
        {
            Assert.Equal("content", notifications.LastModificationKind);
            var summary = Assert.IsType<KitchenOrderModificationSummary>(notifications.LastKitchenChanges);
            Assert.False(summary.NotesChanged);
            var change = Assert.Single(summary.LineNoteChanges);
            Assert.Equal("Arroz paisa familiar", change.ProductName);
            Assert.Equal(2, change.Quantity);
            Assert.Equal(previousNotes, change.PreviousNotes);
            Assert.Equal(notes.Length == 0 ? null : notes, change.Notes);
        }
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
                    GuestName = "Cliente que recoge",
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
                    GuestName = "Cliente que recoge",
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
