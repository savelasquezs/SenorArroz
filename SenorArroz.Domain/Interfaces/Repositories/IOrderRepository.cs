using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Models;
using SenorArroz.Shared.Models;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IOrderRepository
{
    // CRUD básico
    Task<Order?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Order?> GetByIdWithDetailsAsync(int id, CancellationToken cancellationToken = default);
    Task<Order?> GetByIdWithFullDetailsAsync(int id, CancellationToken cancellationToken = default);
    Task<PagedResult<Order>> GetAllAsync(int page, int pageSize, string? sortBy = null, string? sortOrder = "asc", DateTime? fromDate = null, DateTime? toDate = null, int? branchId = null, bool forKitchen = false, CancellationToken cancellationToken = default);
    Task<Order> CreateAsync(Order order, CancellationToken cancellationToken = default);
    Task<Order> UpdateAsync(Order order, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);

    // Filtros y búsquedas
    Task<PagedResult<Order>> GetByBranchAsync(int branchId, int page, int pageSize, string? sortBy = null, string? sortOrder = "asc", CancellationToken cancellationToken = default);
    Task<PagedResult<Order>> GetByCustomerAsync(int customerId, int page, int pageSize, string? sortBy = null, string? sortOrder = "asc", CancellationToken cancellationToken = default);
    Task<PagedResult<Order>> GetByStatusAsync(OrderStatus status, OrderType? typeFilter = null, int? branchId = null, int page = 1, int pageSize = 10, string? sortBy = null, string? sortOrder = "asc", CancellationToken cancellationToken = default);
    Task<PagedResult<Order>> GetByTypeAsync(OrderType type, int? branchId = null, int page = 1, int pageSize = 10, string? sortBy = null, string? sortOrder = "asc", CancellationToken cancellationToken = default);
    Task<PagedResult<Order>> GetByDeliveryManAsync(int deliveryManId, int page, int pageSize, string? sortBy = null, string? sortOrder = "asc", CancellationToken cancellationToken = default);
    Task<PagedResult<Order>> GetByDateRangeAsync(DateTime fromDate, DateTime toDate, int? branchId = null, int page = 1, int pageSize = 10, string? sortBy = null, string? sortOrder = "asc", CancellationToken cancellationToken = default);
    Task<PagedResult<Order>> GetByDateAsync(DateTime date, int? branchId = null, int page = 1, int pageSize = 10, string? sortBy = null, string? sortOrder = "asc", CancellationToken cancellationToken = default);

    // Estados específicos
    Task<List<Order>> GetOrdersInPreparationAsync(int? branchId = null, CancellationToken cancellationToken = default);
    Task<List<Order>> GetReadyOrdersAsync(int? branchId = null, CancellationToken cancellationToken = default);
    Task<List<Order>> GetOrdersOnTheWayAsync(int? branchId = null, CancellationToken cancellationToken = default);
    Task<List<Order>> GetOrdersForDeliveryManAsync(int deliveryManId, CancellationToken cancellationToken = default);
    Task<List<Order>> GetAvailableOrdersForDeliveryAsync(int? branchId = null, CancellationToken cancellationToken = default);
    Task<List<Order>> GetReservationsForDateAsync(DateTime date, int? branchId = null, CancellationToken cancellationToken = default);
    Task<List<Order>> GetUpcomingReservationsAsync(int? branchId = null, int hours = 24, CancellationToken cancellationToken = default);

    // Estadísticas y reportes
    Task<int> GetTotalOrdersCountAsync(int? branchId = null, CancellationToken cancellationToken = default);
    Task<int> GetOrdersCountByStatusAsync(OrderStatus status, int? branchId = null, CancellationToken cancellationToken = default);
    Task<int> GetOrdersCountByTypeAsync(OrderType type, int? branchId = null, CancellationToken cancellationToken = default);
    Task<int> GetActiveOrdersCountForDeliveryManAsync(int deliveryManId, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalSalesAsync(int? branchId = null, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);
    Task<decimal> GetAverageOrderValueAsync(int? branchId = null, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);
    Task<List<Order>> GetTopSellingProductsAsync(int? branchId = null, DateTime? fromDate = null, DateTime? toDate = null, int limit = 10, CancellationToken cancellationToken = default);

    // Validaciones de negocio
    Task<bool> CanAssignDeliveryManAsync(int orderId, int deliveryManId, CancellationToken cancellationToken = default);
    Task<bool> CanCancelOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<bool> CanChangeStatusAsync(int orderId, OrderStatus newStatus, CancellationToken cancellationToken = default);
    Task<bool> HasActiveOrdersAsync(int customerId, CancellationToken cancellationToken = default);
    Task<bool> HasOrdersInProgressAsync(int deliveryManId, CancellationToken cancellationToken = default);

    // Cambios de estado
    Task<Order> ChangeStatusAsync(int orderId, OrderStatus newStatus, string? reason = null, CancellationToken cancellationToken = default);
    Task<Order> AssignDeliveryManAsync(int orderId, int deliveryManId, CancellationToken cancellationToken = default);
    Task<Order> UnassignDeliveryManAsync(int orderId, CancellationToken cancellationToken = default);
    Task<Order> CancelOrderAsync(int orderId, string reason, CancellationToken cancellationToken = default);

    // Búsquedas avanzadas
    Task<PagedResult<Order>> SearchOrdersAsync(
        string? searchTerm = null,
        int? branchId = null,
        int? customerId = null,
        int? deliveryManId = null,
        OrderStatus? status = null,
        OrderType? type = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        decimal? minAmount = null,
        decimal? maxAmount = null,
        int page = 1,
        int pageSize = 10,
        string? sortBy = null,
        string? sortOrder = "asc",
        DateTime? reservedFromDate = null,
        DateTime? reservedToDate = null,
        bool excludeFutureReservations = false,
        int? bankId = null,
        int? neighborhoodId = null,
        bool includeOnsiteActiveInAssignedHistory = false,
        string? totalDigitsPrefix = null,
        int? appId = null,
        bool appPaymentsUnsettledOnly = false,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Pedidos en estado entregado cuyo instante en <c>status_times.delivered</c> (JSON) cae en
    /// <paramref name="fromUtc"/>..<paramref name="toUtc"/> (UTC). Solo <see cref="OrderType.Delivery"/> y
    /// <see cref="OrderType.Onsite"/>; requiere <c>delivery_man_id</c> no nulo.
    /// </summary>
    Task<PagedResult<Order>> SearchDeliveredOrdersByDeliveredAtRangeAsync(
        int? branchId,
        int? deliveryManId,
        OrderType type,
        DateTime fromUtc,
        DateTime toUtc,
        int page = 1,
        int pageSize = 500,
        CancellationToken cancellationToken = default);

    // Reservas
    Task<IEnumerable<Order>> GetReservationsDueForPreparation(
        DateTime fromTime,
        DateTime toTime,
        OrderStatus status,
        CancellationToken cancellationToken = default);

    // Dashboard principal
    Task<PrincipalKpiSnapshot> GetPrincipalKpiSnapshotAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    Task<PrincipalPipelineCounts> GetPrincipalPipelineCountsAsync(
        int? branchId,
        CancellationToken cancellationToken = default);

    Task<List<Order>> GetRecentOrdersForDashboardAsync(
        int? branchId,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Domicilios entregados en el rango (filtra por <see cref="Order.UpdatedAt"/>).
    /// </summary>
    Task<List<Order>> GetDeliveredDeliveryOrdersForDashboardAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        int? deliveryManId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pedidos <b>entregados</b> en el rango (por <see cref="Order.UpdatedAt"/>), solo UpdatedAt + Total,
    /// para alinear ventas diarias con la evolución de domicilios (fees vs ventas).
    /// Si <paramref name="deliveryManId"/> tiene valor, solo pedidos domicilio entregados por ese repartidor
    /// (misma base que el KPI % fees/ventas filtrado).
    /// </summary>
    Task<List<(DateTime UpdatedAt, int Total)>> GetDeliveredOrdersSalesTicksForDashboardAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        int? deliveryManId = null,
        CancellationToken cancellationToken = default);

    // Dashboard ventas
    Task<List<BranchSalesComparisonAggregate>> GetDashboardSalesComparisonAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    Task<List<SalesDayPoint>> GetDashboardSalesByDayAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    Task<List<OrdersDayPoint>> GetDashboardOrdersByDayAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    Task<List<SalesMonthPoint>> GetDashboardSalesByMonthAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    Task<List<OrdersMonthPoint>> GetDashboardOrdersByMonthAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    Task<List<SalesYearPoint>> GetDashboardSalesByYearAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    Task<List<OrdersYearPoint>> GetDashboardOrdersByYearAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    Task<List<SalesHourPoint>> GetDashboardSalesByHourAsync(
        int? branchId,
        DateTime dayStartUtc,
        DateTime dayEndUtc,
        CancellationToken cancellationToken = default);

    Task<List<OrdersHourPoint>> GetDashboardOrdersByHourAsync(
        int? branchId,
        DateTime dayStartUtc,
        DateTime dayEndUtc,
        CancellationToken cancellationToken = default);

    Task<List<SalesProductAggregateRow>> GetSalesProductAggregatesForDashboardAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Ventas por producto incluyendo categoría de producto (mismas reglas que agregado por producto).</summary>
    Task<List<SalesProductCategoryAggregateRow>> GetSalesProductCategoryAggregatesForDashboardAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    Task<List<SalesCategoryAggregateRow>> GetSalesCategoryAggregatesForDashboardAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Peso total vendido (gramos) por categoría de producto en el rango; solo líneas con producto que tenga peso definido.
    /// </summary>
    Task<List<SalesCategoryWeightRow>> GetSalesCategoryWeightAggregatesForDashboardAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gramos vendidos por producto (cantidad × peso) en el rango; solo productos con <c>WeightGrams</c>.
    /// </summary>
    Task<List<SalesProductWeightRow>> GetSalesProductWeightAggregatesForDashboardAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evolución de gramos vendidos para una categoría (solo líneas con peso en producto), agrupada por día, mes o año.
    /// </summary>
    Task<List<SalesCategoryWeightEvolutionPoint>> GetSalesCategoryWeightEvolutionAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        int categoryId,
        CategoryWeightEvolutionGranularity granularity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evolución de gramos por categoría (una serie por categoría con ventas con peso), mismo bucketing día/mes/año.
    /// </summary>
    Task<List<SalesCategoryWeightEvolutionSeries>> GetSalesCategoryWeightEvolutionAllCategoriesAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CategoryWeightEvolutionGranularity granularity,
        CancellationToken cancellationToken = default);

    /// <summary>Pedidos entregados vinculados al cliente (fidelización).</summary>
    Task<int> CountDeliveredOrdersForCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    Task UpdateOrderLoyaltyCycleAsync(int orderId, int? loyaltyCycleStepId, string? loyaltyRewardSnapshot, CancellationToken cancellationToken = default);
}
