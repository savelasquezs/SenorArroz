using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IOrderRepository
{
    // CRUD básico
    Task<Order?> GetByIdAsync(int id);
    Task<Order?> GetByIdWithDetailsAsync(int id);
    Task<Order?> GetByIdWithFullDetailsAsync(int id);
    Task<PagedResult<Order>> GetAllAsync(int page, int pageSize, string? sortBy = null, string? sortOrder = "asc", DateTime? fromDate = null, DateTime? toDate = null, int? branchId = null);
    Task<Order> CreateAsync(Order order);
    Task<Order> UpdateAsync(Order order);
    Task DeleteAsync(int id);

    // Filtros y búsquedas
    Task<PagedResult<Order>> GetByBranchAsync(int branchId, int page, int pageSize, string? sortBy = null, string? sortOrder = "asc");
    Task<PagedResult<Order>> GetByCustomerAsync(int customerId, int page, int pageSize, string? sortBy = null, string? sortOrder = "asc");
    Task<PagedResult<Order>> GetByStatusAsync(OrderStatus status, int? branchId = null, int page = 1, int pageSize = 10, string? sortBy = null, string? sortOrder = "asc");
    Task<PagedResult<Order>> GetByTypeAsync(OrderType type, int? branchId = null, int page = 1, int pageSize = 10, string? sortBy = null, string? sortOrder = "asc");
    Task<PagedResult<Order>> GetByDeliveryManAsync(int deliveryManId, int page, int pageSize, string? sortBy = null, string? sortOrder = "asc");
    Task<PagedResult<Order>> GetByDateRangeAsync(DateTime fromDate, DateTime toDate, int? branchId = null, int page = 1, int pageSize = 10, string? sortBy = null, string? sortOrder = "asc");
    Task<PagedResult<Order>> GetByDateAsync(DateTime date, int? branchId = null, int page = 1, int pageSize = 10, string? sortBy = null, string? sortOrder = "asc");

    // Estados específicos
    Task<List<Order>> GetOrdersInPreparationAsync(int? branchId = null);
    Task<List<Order>> GetReadyOrdersAsync(int? branchId = null);
    Task<List<Order>> GetOrdersOnTheWayAsync(int? branchId = null);
    Task<List<Order>> GetOrdersForDeliveryManAsync(int deliveryManId);
    Task<List<Order>> GetAvailableOrdersForDeliveryAsync(int? branchId = null);
    Task<List<Order>> GetReservationsForDateAsync(DateTime date, int? branchId = null);
    Task<List<Order>> GetUpcomingReservationsAsync(int? branchId = null, int hours = 24);

    // Estadísticas y reportes
    Task<int> GetTotalOrdersCountAsync(int? branchId = null);
    Task<int> GetOrdersCountByStatusAsync(OrderStatus status, int? branchId = null);
    Task<int> GetOrdersCountByTypeAsync(OrderType type, int? branchId = null);
    Task<int> GetActiveOrdersCountForDeliveryManAsync(int deliveryManId);
    Task<decimal> GetTotalSalesAsync(int? branchId = null, DateTime? fromDate = null, DateTime? toDate = null);
    Task<decimal> GetAverageOrderValueAsync(int? branchId = null, DateTime? fromDate = null, DateTime? toDate = null);
    Task<List<Order>> GetTopSellingProductsAsync(int? branchId = null, DateTime? fromDate = null, DateTime? toDate = null, int limit = 10);

    // Validaciones de negocio
    Task<bool> CanAssignDeliveryManAsync(int orderId, int deliveryManId);
    Task<bool> CanCancelOrderAsync(int orderId);
    Task<bool> CanChangeStatusAsync(int orderId, OrderStatus newStatus);
    Task<bool> HasActiveOrdersAsync(int customerId);
    Task<bool> HasOrdersInProgressAsync(int deliveryManId);

    // Cambios de estado
    Task<Order> ChangeStatusAsync(int orderId, OrderStatus newStatus, string? reason = null);
    Task<Order> AssignDeliveryManAsync(int orderId, int deliveryManId);
    Task<Order> UnassignDeliveryManAsync(int orderId);
    Task<Order> CancelOrderAsync(int orderId, string reason);

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
        string? sortOrder = "asc"
    );
}
