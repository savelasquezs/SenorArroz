using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Kitchen;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Constants;

namespace SenorArroz.Tests;

/// <summary>
/// Regresión: reserva → ready debe llamar a UpdateAsync con líneas cargadas (antes se borraban todas).
/// </summary>
public class ChangeOrderStatusHandlerReservationTests
{
    private sealed class TestCurrentUser : ICurrentUser
    {
        private readonly string _role;

        public TestCurrentUser(string role = Roles.Kitchen)
        {
            _role = role;
        }

        public int Id => 1;
        public string Role => _role;
        public int BranchId => 1;
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
        public Task NotifyOrderModifiedToKitchen(OrderDto order, string modificationKind, KitchenOrderModificationSummary? kitchenChanges = null) => Task.CompletedTask;
        public Task NotifyOrderModifiedToDelivery(OrderDto order, string modificationKind, KitchenOrderModificationSummary? kitchenChanges = null)
        {
            DeliveryModifiedCalls++;
            LastDeliveryModifiedOrder = order;
            LastDeliveryModificationKind = modificationKind;
            LastDeliveryKitchenChanges = kitchenChanges;
            return Task.CompletedTask;
        }
        public Task NotifyOrderCancelledToKitchen(int branchId, int orderId, string? reasonPreview = null) => Task.CompletedTask;
        public Task NotifyDeliverymanLocation(int branchId, int deliverymanId, int? deliveryRouteId, double latitude, double longitude, DateTime recordedAt) => Task.CompletedTask;
    }

    private sealed class NullDeliveryRouteWorkflow : IDeliveryRouteWorkflowService
    {
        public Task OnOrderAssignedToDeliverymanAsync(Order order, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task OnOrderUnassignedAsync(int orderId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task OnOrderCancelledWhileRouteOpenAsync(int orderId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TryCompleteInProgressRouteAsync(int orderId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TryFinalizeRouteWhenAllTerminalAsync(int orderId, int? routeIdIfOrderUnlinked = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> DeliverymanHasPendingOrdersOnActiveRouteAsync(int deliverymanId, int branchId, CancellationToken cancellationToken = default, IReadOnlyCollection<int>? excludeOrderIds = null) => Task.FromResult(false);
        public Task<int> ConsolidatePendingRoutesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class NullPrintQueue : IPrintQueueService
    {
        public Task<bool> IsAgentTokenValidAsync(int branchId, string? plainToken, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<PrintJob> EnqueueAsync(int branchId, PrintJobKind kind, IReadOnlyList<int> orderIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PrintJob { Id = 1, BranchId = branchId, Kind = kind, Status = PrintJobStatus.Pending });
        public Task<PrintJob> EnqueueDeliveryAsync(int branchId, IReadOnlyList<int> orderIds, int? deliverymanUserId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PrintJob { Id = 1, BranchId = branchId, Kind = PrintJobKind.Delivery, Status = PrintJobStatus.Pending });
        public Task<PrintJob> EnqueueTestPrintAsync(int branchId, PrintJobKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PrintJob { Id = 1, BranchId = branchId, Kind = kind, Status = PrintJobStatus.Pending });
        public Task ValidateDeliverymanDeliveryEnqueueAsync(int branchId, int deliverymanUserId, IReadOnlyList<int> orderIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<PrintJobAgentItemDto>> ClaimPendingForAgentAsync(int branchId, IReadOnlyList<PrintJobKind> kinds, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PrintJobAgentItemDto>>(Array.Empty<PrintJobAgentItemDto>());
        public Task<PrintJobAgentItemDto?> ClaimSpecificForAgentAsync(int branchId, long jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PrintJobAgentItemDto?>(null);
        public Task<PrintJobStatusDto?> GetJobStatusAsync(int branchId, long jobId, int? deliverymanUserId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<PrintJobStatusDto?>(null);
        public Task<bool> TryCompleteJobAsync(int branchId, long jobId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> TryFailJobAsync(int branchId, long jobId, string message, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class NullLoyaltyCycle : ILoyaltyCycleService
    {
        public Task ApplyLoyaltyPreviewToCustomerDtoAsync(CustomerDto dto, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task OnOrderDeliveredAsync(int orderId, int branchId, int? customerId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task OnOrderLeftDeliveredAsync(int orderId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static ChangeOrderStatusHandler BuildHandler(
        IOrderRepository repo,
        IClock clock,
        ICurrentUser? user = null,
        IOrderNotificationService? notifications = null,
        IPrintQueueService? printQueue = null,
        IExternalDeliveryStatusSyncService? externalDeliveryStatusSync = null)
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.AddMaps(typeof(ChangeOrderStatusCommand).Assembly);
        }, NullLoggerFactory.Instance).CreateMapper();
        var businessRules = new OrderBusinessRulesService(clock);
        return new ChangeOrderStatusHandler(
            repo,
            mapper,
            user ?? new TestCurrentUser(),
            businessRules,
            notifications ?? new NullOrderNotifications(),
            new NullDeliveryRouteWorkflow(),
            printQueue ?? new NullPrintQueue(),
            new NullLoyaltyCycle(),
            NullLogger<ChangeOrderStatusHandler>.Instance,
            externalDeliveryStatusSync);
    }

    private static (Order summary, Order detailed) ReservationPair(int orderId, int? addressId)
    {
        var utc = DateTime.UtcNow;
        var branch = new Branch
        {
            Id = 1,
            Name = "Sucursal",
            Address = "A",
            Phone1 = "1",
            CreatedAt = utc,
            UpdatedAt = utc,
        };
        var takenBy = new User
        {
            Id = 1,
            BranchId = 1,
            Name = "U",
            Email = "u@test",
            Phone = "1",
            PasswordHash = "x",
            Branch = branch,
            CreatedAt = utc,
            UpdatedAt = utc,
        };

        var summary = new Order
        {
            Id = orderId,
            BranchId = 1,
            TakenById = 1,
            Type = OrderType.Reservation,
            Status = OrderStatus.Taken,
            StatusTimes = "{}",
            AddressId = addressId,
            GuestName = "G",
            Branch = branch,
            TakenBy = takenBy,
            CreatedAt = utc,
            UpdatedAt = utc,
        };

        var line = new OrderDetail
        {
            Id = 50,
            OrderId = orderId,
            ProductId = 9,
            Quantity = 2,
            UnitPrice = 5000,
            Discount = 0,
            Subtotal = 10_000,
            Product = new Product
            {
                Id = 9,
                CategoryId = 1,
                Name = "Plato",
                Price = 5000,
                Category = new ProductCategory { Id = 1, BranchId = 1, Name = "C", Branch = branch },
            },
            CreatedAt = utc,
            UpdatedAt = utc,
        };

        var detailed = new Order
        {
            Id = orderId,
            BranchId = 1,
            TakenById = 1,
            Type = OrderType.Reservation,
            Status = OrderStatus.Taken,
            StatusTimes = "{}",
            AddressId = addressId,
            GuestName = "G",
            Branch = branch,
            TakenBy = takenBy,
            OrderDetails = new List<OrderDetail> { line },
            CreatedAt = utc,
            UpdatedAt = utc,
        };

        return (summary, detailed);
    }

    [Fact]
    public async Task Reservation_to_ready_uses_details_load_and_keeps_lines_for_onsite()
    {
        const int orderId = 42;
        var (summary, detailed) = ReservationPair(orderId, addressId: null);
        summary.Status = OrderStatus.InPreparation;
        detailed.Status = OrderStatus.InPreparation;
        var mock = new Mock<IOrderRepository>(MockBehavior.Strict);

        mock.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(summary);
        mock.Setup(r => r.GetByIdWithDetailsAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(detailed);
        mock.Setup(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Returns((Order o, CancellationToken _) =>
            {
                detailed.Type = o.Type;
                return Task.FromResult(o);
            });
        mock.Setup(r => r.ChangeStatusAsync(orderId, OrderStatus.Ready, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                detailed.Status = OrderStatus.Ready;
                return Task.FromResult(detailed);
            });

        var handler = BuildHandler(mock.Object, new FakeClock(DateTime.UtcNow));
        await handler.Handle(
            new ChangeOrderStatusCommand
            {
                Id = orderId,
                StatusChange = new ChangeOrderStatusDto { Status = OrderStatus.Ready },
            },
            CancellationToken.None);

        mock.Verify(r => r.GetByIdWithDetailsAsync(orderId, It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(r => r.UpdateAsync(
            It.Is<Order>(o => o.Type == OrderType.Onsite && o.OrderDetails.Count == 1 && o.OrderDetails.First().Id == 50),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reservation_to_ready_sets_delivery_when_address_present()
    {
        const int orderId = 43;
        var (summary, detailed) = ReservationPair(orderId, addressId: 100);
        summary.Status = OrderStatus.InPreparation;
        detailed.Status = OrderStatus.InPreparation;
        var mock = new Mock<IOrderRepository>(MockBehavior.Strict);

        mock.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(summary);
        mock.Setup(r => r.GetByIdWithDetailsAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(detailed);
        mock.Setup(r => r.UpdateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Returns((Order o, CancellationToken _) =>
            {
                detailed.Type = o.Type;
                return Task.FromResult(o);
            });
        mock.Setup(r => r.ChangeStatusAsync(orderId, OrderStatus.Ready, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                detailed.Status = OrderStatus.Ready;
                return Task.FromResult(detailed);
            });

        var handler = BuildHandler(mock.Object, new FakeClock(DateTime.UtcNow));
        await handler.Handle(
            new ChangeOrderStatusCommand
            {
                Id = orderId,
                StatusChange = new ChangeOrderStatusDto { Status = OrderStatus.Ready },
            },
            CancellationToken.None);

        mock.Verify(r => r.UpdateAsync(
            It.Is<Order>(o => o.Type == OrderType.Delivery && o.OrderDetails.Count == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Admin_delivery_change_from_on_the_way_notifies_only_the_affected_order()
    {
        const int orderId = 87;
        var utc = new DateTime(2026, 8, 10, 15, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            Id = orderId,
            BranchId = 1,
            TakenById = 1,
            Type = OrderType.Delivery,
            Status = OrderStatus.OnTheWay,
            DeliveryManId = 24,
            StatusTimes = "{}",
            CreatedAt = utc,
            UpdatedAt = utc,
        };
        var repo = new Mock<IOrderRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        repo.Setup(r => r.ChangeStatusAsync(orderId, OrderStatus.Delivered, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                order.Status = OrderStatus.Delivered;
                return order;
            });
        var notifications = new NullOrderNotifications();
        var handler = BuildHandler(
            repo.Object,
            new FakeClock(utc),
            new TestCurrentUser(Roles.Admin),
            notifications);

        var result = await handler.Handle(
            new ChangeOrderStatusCommand
            {
                Id = orderId,
                StatusChange = new ChangeOrderStatusDto { Status = OrderStatus.Delivered },
            },
            CancellationToken.None);

        Assert.Equal(OrderStatus.Delivered, result.Status);
        Assert.Equal(1, notifications.DeliveryModifiedCalls);
        Assert.Equal(orderId, notifications.LastDeliveryModifiedOrder?.Id);
        Assert.Equal(OrderStatus.Delivered, notifications.LastDeliveryModifiedOrder?.Status);
        Assert.Equal("status", notifications.LastDeliveryModificationKind);
        Assert.Null(notifications.LastDeliveryKitchenChanges);
    }

    [Fact]
    public async Task Rappi_order_ready_prints_in_kitchen_and_syncs_ready_for_pickup()
    {
        const int orderId = 118;
        var utc = new DateTime(2026, 8, 14, 18, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            Id = orderId,
            BranchId = 1,
            TakenById = 1,
            Type = OrderType.Delivery,
            Status = OrderStatus.InPreparation,
            ExternalFulfillmentProvider = "rappi",
            ExternalOrderId = "rappi-118",
            StatusTimes = "{}",
            CreatedAt = utc,
            UpdatedAt = utc,
        };
        var repo = new Mock<IOrderRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetByIdAsync(orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        repo.Setup(r => r.ChangeStatusAsync(
                orderId,
                OrderStatus.Ready,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                order.Status = OrderStatus.Ready;
                return order;
            });
        var printQueue = new Mock<IPrintQueueService>();
        printQueue.Setup(x => x.EnqueueAsync(
                order.BranchId,
                PrintJobKind.Kitchen,
                It.Is<IReadOnlyList<int>>(ids => ids.SequenceEqual(new[] { orderId })),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrintJob());
        var externalSync = new Mock<IExternalDeliveryStatusSyncService>();
        externalSync.Setup(x => x.SyncReadyForPickupAsync(
                orderId,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var handler = BuildHandler(
            repo.Object,
            new FakeClock(utc),
            new TestCurrentUser(Roles.Admin),
            printQueue: printQueue.Object,
            externalDeliveryStatusSync: externalSync.Object);

        var result = await handler.Handle(
            new ChangeOrderStatusCommand
            {
                Id = orderId,
                StatusChange = new ChangeOrderStatusDto { Status = OrderStatus.Ready },
            },
            CancellationToken.None);

        Assert.Equal(OrderStatus.Ready, result.Status);
        printQueue.VerifyAll();
        externalSync.VerifyAll();
    }
}
