using AutoMapper;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public class CreateOrderHandlerTests
{
    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeCurrentUser(string role, int branchId = 1) : ICurrentUser
    {
        public int Id => 99;
        public string Role => role;
        public int BranchId => branchId;
        public bool IsAuthenticated => true;
    }

    private sealed class NullOrderRepository : IOrderRepository
    {
        public Task<Order?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Order?> GetByIdWithDetailsAsync(int id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Order?> GetByIdWithFullDetailsAsync(int id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<Order>> GetAllAsync(int page, int pageSize, string? sortBy = null, string? sortOrder = "asc", DateTime? fromDate = null, DateTime? toDate = null, int? branchId = null, bool forKitchen = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Order> CreateAsync(Order order, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Order> UpdateAsync(Order order, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteAsync(int id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<Order>> GetByBranchAsync(int branchId, int page, int pageSize, string? sortBy = null, string? sortOrder = "asc", CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<Order>> GetByCustomerAsync(int customerId, int page, int pageSize, string? sortBy = null, string? sortOrder = "asc", CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<Order>> GetByStatusAsync(OrderStatus status, OrderType? typeFilter = null, int? branchId = null, int page = 1, int pageSize = 10, string? sortBy = null, string? sortOrder = "asc", CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<Order>> GetByTypeAsync(OrderType type, int? branchId = null, int page = 1, int pageSize = 10, string? sortBy = null, string? sortOrder = "asc", CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<Order>> GetByDeliveryManAsync(int deliveryManId, int page, int pageSize, string? sortBy = null, string? sortOrder = "asc", CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<Order>> GetByDateRangeAsync(DateTime fromDate, DateTime toDate, int? branchId = null, int page = 1, int pageSize = 10, string? sortBy = null, string? sortOrder = "asc", CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<Order>> GetByDateAsync(DateTime date, int? branchId = null, int page = 1, int pageSize = 10, string? sortBy = null, string? sortOrder = "asc", CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<Order>> SearchOrdersAsync(string? searchTerm = null, int? branchId = null, int? customerId = null, int? deliveryManId = null, OrderStatus? status = null, OrderType? type = null, DateTime? fromDate = null, DateTime? toDate = null, decimal? minAmount = null, decimal? maxAmount = null, int page = 1, int pageSize = 10, string? sortBy = null, string? sortOrder = "asc", DateTime? reservedFromDate = null, DateTime? reservedToDate = null, bool excludeFutureReservations = false, int? bankId = null, int? neighborhoodId = null, bool includeOnsiteActiveInAssignedHistory = false, string? totalDigitsPrefix = null, int? appId = null, bool appPaymentsUnsettledOnly = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<Order>> SearchDeliveredOrdersByDeliveredAtRangeAsync(int? branchId, int? deliveryManId, OrderType type, DateTime fromUtc, DateTime toUtc, int page = 1, int pageSize = 500, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Order>> GetOrdersInPreparationAsync(int? branchId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Order>> GetReadyOrdersAsync(int? branchId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Order>> GetOrdersOnTheWayAsync(int? branchId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Order>> GetOrdersForDeliveryManAsync(int deliveryManId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Order>> GetAvailableOrdersForDeliveryAsync(int? branchId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Order>> GetReservationsForDateAsync(DateTime date, int? branchId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Order>> GetUpcomingReservationsAsync(int? branchId = null, int hours = 24, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> GetTotalOrdersCountAsync(int? branchId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> GetOrdersCountByStatusAsync(OrderStatus status, int? branchId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> GetOrdersCountByTypeAsync(OrderType type, int? branchId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> GetActiveOrdersCountForDeliveryManAsync(int deliveryManId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<decimal> GetTotalSalesAsync(int? branchId = null, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<decimal> GetAverageOrderValueAsync(int? branchId = null, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Order>> GetTopSellingProductsAsync(int? branchId = null, DateTime? fromDate = null, DateTime? toDate = null, int limit = 10, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> CanAssignDeliveryManAsync(int orderId, int deliveryManId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> CanCancelOrderAsync(int orderId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> CanChangeStatusAsync(int orderId, OrderStatus newStatus, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> HasActiveOrdersAsync(int customerId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> HasOrdersInProgressAsync(int deliveryManId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Order> ChangeStatusAsync(int orderId, OrderStatus newStatus, string? reason = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Order> AssignDeliveryManAsync(int orderId, int deliveryManId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Order> UnassignDeliveryManAsync(int orderId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Order> CancelOrderAsync(int orderId, string reason, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<Order>> GetReservationsDueForPreparation(DateTime fromTime, DateTime toTime, OrderStatus status, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Domain.Models.PrincipalKpiSnapshot> GetPrincipalKpiSnapshotAsync(int? branchId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Domain.Models.PrincipalPipelineCounts> GetPrincipalPipelineCountsAsync(int? branchId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Order>> GetRecentOrdersForDashboardAsync(int? branchId, int take, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Order>> GetDeliveredDeliveryOrdersForDashboardAsync(int? branchId, DateTime fromUtc, DateTime toUtc, int? deliveryManId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<ValueTuple<DateTime, int>>> GetDeliveredOrdersSalesTicksForDashboardAsync(int? branchId, DateTime fromUtc, DateTime toUtc, int? deliveryManId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Domain.Models.BranchSalesComparisonAggregate>> GetDashboardSalesComparisonAsync(int? branchId, DateTime fromUtc, DateTime toUtc, int? dayOfWeek = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Domain.Models.SalesDayPoint>> GetDashboardSalesByDayAsync(int? branchId, DateTime fromUtc, DateTime toUtc, int? dayOfWeek = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Domain.Models.OrdersDayPoint>> GetDashboardOrdersByDayAsync(int? branchId, DateTime fromUtc, DateTime toUtc, int? dayOfWeek = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Domain.Models.SalesMonthPoint>> GetDashboardSalesByMonthAsync(int? branchId, DateTime fromUtc, DateTime toUtc, int? dayOfWeek = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Domain.Models.OrdersMonthPoint>> GetDashboardOrdersByMonthAsync(int? branchId, DateTime fromUtc, DateTime toUtc, int? dayOfWeek = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Domain.Models.SalesYearPoint>> GetDashboardSalesByYearAsync(int? branchId, DateTime fromUtc, DateTime toUtc, int? dayOfWeek = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Domain.Models.OrdersYearPoint>> GetDashboardOrdersByYearAsync(int? branchId, DateTime fromUtc, DateTime toUtc, int? dayOfWeek = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Domain.Models.SalesHourPoint>> GetDashboardSalesByHourAsync(int? branchId, DateTime dayStartUtc, DateTime dayEndUtc, int? dayOfWeek = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Domain.Models.OrdersHourPoint>> GetDashboardOrdersByHourAsync(int? branchId, DateTime dayStartUtc, DateTime dayEndUtc, int? dayOfWeek = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Domain.Models.SalesHourlyAnalyticsPoint>> GetDashboardSalesHourlyAnalyticsAsync(int? branchId, DateTime fromUtc, DateTime toUtc, int? dayOfWeek = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Domain.Models.SalesDailyHourBucket>> GetDashboardSalesDailyHourBucketsAsync(int? branchId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Domain.Models.SalesProductAggregateRow>> GetSalesProductAggregatesForDashboardAsync(int? branchId, DateTime fromUtc, DateTime toUtc, int? dayOfWeek = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Domain.Models.SalesProductCategoryAggregateRow>> GetSalesProductCategoryAggregatesForDashboardAsync(int? branchId, DateTime fromUtc, DateTime toUtc, int? dayOfWeek = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Domain.Models.SalesCategoryAggregateRow>> GetSalesCategoryAggregatesForDashboardAsync(int? branchId, DateTime fromUtc, DateTime toUtc, int? dayOfWeek = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Domain.Models.SalesCategoryWeightRow>> GetSalesCategoryWeightAggregatesForDashboardAsync(int? branchId, DateTime fromUtc, DateTime toUtc, int? dayOfWeek = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Domain.Models.SalesProductWeightRow>> GetSalesProductWeightAggregatesForDashboardAsync(int? branchId, DateTime fromUtc, DateTime toUtc, int? dayOfWeek = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Domain.Models.SalesCategoryWeightEvolutionPoint>> GetSalesCategoryWeightEvolutionAsync(int? branchId, DateTime fromUtc, DateTime toUtc, int categoryId, Domain.Enums.CategoryWeightEvolutionGranularity granularity, int? dayOfWeek = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<Domain.Models.SalesCategoryWeightEvolutionSeries>> GetSalesCategoryWeightEvolutionAllCategoriesAsync(int? branchId, DateTime fromUtc, DateTime toUtc, Domain.Enums.CategoryWeightEvolutionGranularity granularity, int? dayOfWeek = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CountDeliveredOrdersForCustomerAsync(int customerId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateOrderLoyaltyCycleAsync(int orderId, int? loyaltyCycleStepId, string? loyaltyRewardSnapshot, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class NullNotificationService : IOrderNotificationService
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

    private static ApplicationDbContext CreateInMemoryContext(string name)
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new ApplicationDbContext(opts);
    }

    private static CreateOrderHandler BuildHandler(ICurrentUser currentUser, ApplicationDbContext db)
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.AddMaps(typeof(SenorArroz.Application.Features.Orders.Commands.CreateOrderCommand).Assembly);
        }, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
            .CreateMapper();

        return new CreateOrderHandler(
            new NullOrderRepository(),
            db,
            mapper,
            currentUser,
            new NullNotificationService(),
            new SystemUtcClock());
    }

    private static CreateOrderDto MinimalDeliveryOrder() => new()
    {
        Type = OrderType.Delivery,
        BranchId = 1,
        CustomerId = 1,
        AddressId = 1,
        GuestName = "Test",
        OrderDetails = [new CreateOrderDetailDto { ProductId = 1, Quantity = 1, UnitPrice = 5000 }]
    };

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Superadmin_without_branchId_throws_BusinessException()
    {
        using var db = CreateInMemoryContext(nameof(Superadmin_without_branchId_throws_BusinessException));
        var handler = BuildHandler(new FakeCurrentUser(Roles.Superadmin), db);

        var command = new CreateOrderCommand { Order = new CreateOrderDto { BranchId = 0, OrderDetails = [] } };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            handler.Handle(command, CancellationToken.None));
        Assert.Contains("sucursal", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_role_throws_BusinessException()
    {
        using var db = CreateInMemoryContext(nameof(Unknown_role_throws_BusinessException));
        var handler = BuildHandler(new FakeCurrentUser(Roles.Deliveryman), db);

        var command = new CreateOrderCommand { Order = new CreateOrderDto { BranchId = 1, OrderDetails = [] } };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            handler.Handle(command, CancellationToken.None));
        Assert.Contains("permisos", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Order_without_details_throws_BusinessException()
    {
        using var db = CreateInMemoryContext(nameof(Order_without_details_throws_BusinessException));
        var handler = BuildHandler(new FakeCurrentUser(Roles.Cashier, branchId: 1), db);

        var command = new CreateOrderCommand { Order = new CreateOrderDto { Type = OrderType.Onsite, OrderDetails = [] } };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            handler.Handle(command, CancellationToken.None));
        Assert.Contains("producto", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Delivery_order_without_customer_throws_BusinessException()
    {
        using var db = CreateInMemoryContext(nameof(Delivery_order_without_customer_throws_BusinessException));
        var handler = BuildHandler(new FakeCurrentUser(Roles.Cashier, branchId: 1), db);

        var dto = MinimalDeliveryOrder();
        dto.CustomerId = null;
        var command = new CreateOrderCommand { Order = dto };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            handler.Handle(command, CancellationToken.None));
        Assert.Contains("cliente", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Delivery_order_without_address_throws_BusinessException()
    {
        using var db = CreateInMemoryContext(nameof(Delivery_order_without_address_throws_BusinessException));
        var handler = BuildHandler(new FakeCurrentUser(Roles.Cashier, branchId: 1), db);

        var dto = MinimalDeliveryOrder();
        dto.AddressId = null;
        var command = new CreateOrderCommand { Order = dto };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            handler.Handle(command, CancellationToken.None));
        Assert.Contains("direcci", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reservation_order_without_reservedFor_throws_BusinessException()
    {
        using var db = CreateInMemoryContext(nameof(Reservation_order_without_reservedFor_throws_BusinessException));
        var handler = BuildHandler(new FakeCurrentUser(Roles.Cashier, branchId: 1), db);

        var command = new CreateOrderCommand
        {
            Order = new CreateOrderDto
            {
                Type = OrderType.Reservation,
                GuestName = "Test",
                ReservedFor = null,
                OrderDetails = [new CreateOrderDetailDto { ProductId = 1, Quantity = 1, UnitPrice = 5000 }]
            }
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            handler.Handle(command, CancellationToken.None));
        Assert.Contains("reserva", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareAt_after_reservedFor_throws_BusinessException()
    {
        using var db = CreateInMemoryContext(nameof(PrepareAt_after_reservedFor_throws_BusinessException));
        var handler = BuildHandler(new FakeCurrentUser(Roles.Cashier, branchId: 1), db);

        var reservedFor = DateTime.UtcNow.AddHours(2);
        var command = new CreateOrderCommand
        {
            Order = new CreateOrderDto
            {
                Type = OrderType.Reservation,
                GuestName = "Test",
                ReservedFor = reservedFor,
                PrepareAt = reservedFor.AddHours(1),
                OrderDetails = [new CreateOrderDetailDto { ProductId = 1, Quantity = 1, UnitPrice = 5000 }]
            }
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            handler.Handle(command, CancellationToken.None));
        Assert.Contains("preparaci", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
