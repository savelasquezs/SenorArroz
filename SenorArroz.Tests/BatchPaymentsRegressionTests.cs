using AutoMapper;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

/// <summary>
/// Verifica que CreateOrderHandler persiste todos los pagos en un único batch
/// (AddRange + SaveChangesAsync) en lugar de N roundtrips individuales.
/// </summary>
public class BatchPaymentsRegressionTests
{
    // ── Fakes ─────────────────────────────────────────────────────────────────

    private sealed class BatchFakeCurrentUser(string role = Roles.Cashier, int branchId = 1) : ICurrentUser
    {
        public int Id => 1;
        public string Role => role;
        public int BranchId => branchId;
        public bool IsAuthenticated => true;
    }

    private sealed class BatchSilentNotificationService : IOrderNotificationService
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

    /// <summary>
    /// Implementa únicamente CreateAsync y GetByIdWithFullDetailsAsync.
    /// El resto lanza NotImplementedException porque el handler no los invoca.
    /// </summary>
    private sealed class StubOrderRepository(ApplicationDbContext db) : IOrderRepository
    {
        public async Task<Order> CreateAsync(Order order, CancellationToken cancellationToken = default)
        {
            db.Orders.Add(order);
            await db.SaveChangesAsync(cancellationToken);
            return order;
        }

        public Task<Order?> GetByIdWithFullDetailsAsync(int id, CancellationToken cancellationToken = default)
        {
            var order = new Order
            {
                Id = id,
                BranchId = 1,
                TakenById = 1,
                Status = OrderStatus.Taken,
                Branch = new Branch { Name = "Stub", Address = "-", Phone1 = "-" },
                TakenBy = new User { Name = "Stub", Email = "stub@test.com" },
                OrderDetails = [],
                BankPayments = [],
                AppPayments = []
            };
            return Task.FromResult<Order?>(order);
        }

        // Los métodos restantes no son invocados por el handler en estos tests:
        public Task<Order?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Order?> GetByIdWithDetailsAsync(int id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagedResult<Order>> GetAllAsync(int page, int pageSize, string? sortBy = null, string? sortOrder = "asc", DateTime? fromDate = null, DateTime? toDate = null, int? branchId = null, bool forKitchen = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateCtx(string dbName)
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(opts);
    }

    private static CreateOrderHandler BuildHandler(ApplicationDbContext db, string role = Roles.Cashier, int branchId = 1)
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.AddMaps(typeof(CreateOrderCommand).Assembly);
        }, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance).CreateMapper();

        return new CreateOrderHandler(
            new StubOrderRepository(db),
            db,
            mapper,
            new BatchFakeCurrentUser(role, branchId),
            new BatchSilentNotificationService(),
            new SystemUtcClock());
    }

    private static CreateOrderDto OnsiteOrderWith(
        List<CreateOrderBankPaymentDto>? bank = null,
        List<CreateOrderAppPaymentDto>? app = null) => new()
    {
        Type = OrderType.Onsite,
        BranchId = 1,
        OrderDetails = [new CreateOrderDetailDto { ProductId = 1, Quantity = 1, UnitPrice = 5000 }],
        BankPayments = bank ?? [],
        AppPayments = app ?? []
    };

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BatchPayments_TwoBankPayments_BothAreSaved()
    {
        using var db = CreateCtx(nameof(BatchPayments_TwoBankPayments_BothAreSaved));
        var handler = BuildHandler(db);

        var dto = OnsiteOrderWith(bank:
        [
            new CreateOrderBankPaymentDto { BankId = 1, Amount = 10000 },
            new CreateOrderBankPaymentDto { BankId = 2, Amount = 5000 }
        ]);

        await handler.Handle(new CreateOrderCommand { Order = dto }, CancellationToken.None);

        Assert.Equal(2, db.BankPayments.Count());
        Assert.Equal(0, db.AppPayments.Count());
        Assert.All(db.BankPayments.ToList(), bp => Assert.False(bp.IsVerified));
    }

    [Fact]
    public async Task BatchPayments_TwoAppPayments_BothAreSaved()
    {
        using var db = CreateCtx(nameof(BatchPayments_TwoAppPayments_BothAreSaved));
        var handler = BuildHandler(db);

        var dto = OnsiteOrderWith(app:
        [
            new CreateOrderAppPaymentDto { AppId = 1, Amount = 15000 },
            new CreateOrderAppPaymentDto { AppId = 2, Amount = 8000 }
        ]);

        await handler.Handle(new CreateOrderCommand { Order = dto }, CancellationToken.None);

        Assert.Equal(0, db.BankPayments.Count());
        Assert.Equal(2, db.AppPayments.Count());
        Assert.All(db.AppPayments.ToList(), ap => Assert.False(ap.IsSetted));
    }

    [Fact]
    public async Task BatchPayments_Mixed_AllAreSavedWithCorrectOrderId()
    {
        using var db = CreateCtx(nameof(BatchPayments_Mixed_AllAreSavedWithCorrectOrderId));
        var handler = BuildHandler(db);

        var dto = OnsiteOrderWith(
            bank: [new CreateOrderBankPaymentDto { BankId = 1, Amount = 20000 }],
            app: [new CreateOrderAppPaymentDto { AppId = 1, Amount = 12000 }]
        );

        await handler.Handle(new CreateOrderCommand { Order = dto }, CancellationToken.None);

        var orderId = db.Orders.Single().Id;
        Assert.Equal(1, db.BankPayments.Count());
        Assert.Equal(1, db.AppPayments.Count());
        Assert.Equal(orderId, db.BankPayments.Single().OrderId);
        Assert.Equal(orderId, db.AppPayments.Single().OrderId);
    }

    [Fact]
    public async Task BatchPayments_NoPayments_NothingIsSaved()
    {
        using var db = CreateCtx(nameof(BatchPayments_NoPayments_NothingIsSaved));
        var handler = BuildHandler(db);

        await handler.Handle(
            new CreateOrderCommand { Order = OnsiteOrderWith() },
            CancellationToken.None);

        Assert.Equal(0, db.BankPayments.Count());
        Assert.Equal(0, db.AppPayments.Count());
    }
}
