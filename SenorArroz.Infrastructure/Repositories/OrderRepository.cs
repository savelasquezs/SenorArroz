using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Infrastructure.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly ApplicationDbContext _context;

    public OrderRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Order?> GetByIdAsync(int id)
    {
        return await _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.LoyaltyRule)
            .Include(o => o.DeliveryMan)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<Order?> GetByIdWithDetailsAsync(int id)
    {
        return await _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.LoyaltyRule)
            .Include(o => o.DeliveryMan)
            .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<Order?> GetByIdWithFullDetailsAsync(int id)
    {
        return await _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.LoyaltyRule)
            .Include(o => o.DeliveryMan)
            .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
            .Include(o => o.BankPayments)
            .Include(o => o.AppPayments)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<PagedResult<Order>> GetAllAsync(int page, int pageSize, string? sortBy = null, string? sortOrder = "asc")
    {
        var query = _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.LoyaltyRule)
            .Include(o => o.DeliveryMan)
            .AsQueryable();

        // Aplicar ordenamiento
        query = ApplySorting(query, sortBy, sortOrder);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Order>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        };
    }

    public async Task<Order> CreateAsync(Order order)
    {
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();
        return order;
    }

    public async Task<Order> UpdateAsync(Order order)
    {
        _context.Orders.Update(order);
        await _context.SaveChangesAsync();
        return order;
    }

    public async Task DeleteAsync(int id)
    {
        var order = await _context.Orders.FindAsync(id);
        if (order != null)
        {
            _context.Orders.Remove(order);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<PagedResult<Order>> GetByBranchAsync(int branchId, int page, int pageSize, string? sortBy = null, string? sortOrder = "asc")
    {
        var query = _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.LoyaltyRule)
            .Include(o => o.DeliveryMan)
            .Where(o => o.BranchId == branchId)
            .AsQueryable();

        query = ApplySorting(query, sortBy, sortOrder);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Order>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        };
    }

    public async Task<PagedResult<Order>> GetByCustomerAsync(int customerId, int page, int pageSize, string? sortBy = null, string? sortOrder = "asc")
    {
        var query = _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.LoyaltyRule)
            .Include(o => o.DeliveryMan)
            .Where(o => o.CustomerId == customerId)
            .AsQueryable();

        query = ApplySorting(query, sortBy, sortOrder);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Order>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        };
    }

    public async Task<PagedResult<Order>> GetByStatusAsync(OrderStatus status, int? branchId = null, int page = 1, int pageSize = 10, string? sortBy = null, string? sortOrder = "asc")
    {
        var query = _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.LoyaltyRule)
            .Include(o => o.DeliveryMan)
            .Where(o => o.Status == status)
            .AsQueryable();

        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        query = ApplySorting(query, sortBy, sortOrder);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Order>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        };
    }

    public async Task<PagedResult<Order>> GetByTypeAsync(OrderType type, int? branchId = null, int page = 1, int pageSize = 10, string? sortBy = null, string? sortOrder = "asc")
    {
        var query = _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.LoyaltyRule)
            .Include(o => o.DeliveryMan)
            .Where(o => o.Type == type)
            .AsQueryable();

        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        query = ApplySorting(query, sortBy, sortOrder);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Order>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        };
    }

    public async Task<PagedResult<Order>> GetByDeliveryManAsync(int deliveryManId, int page, int pageSize, string? sortBy = null, string? sortOrder = "asc")
    {
        var query = _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.LoyaltyRule)
            .Include(o => o.DeliveryMan)
            .Where(o => o.DeliveryManId == deliveryManId)
            .AsQueryable();

        query = ApplySorting(query, sortBy, sortOrder);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Order>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        };
    }

    public async Task<PagedResult<Order>> GetByDateRangeAsync(DateTime fromDate, DateTime toDate, int? branchId = null, int page = 1, int pageSize = 10, string? sortBy = null, string? sortOrder = "asc")
    {
        var query = _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.LoyaltyRule)
            .Include(o => o.DeliveryMan)
            .Where(o => o.CreatedAt.Date >= fromDate.Date && o.CreatedAt.Date <= toDate.Date)
            .AsQueryable();

        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        query = ApplySorting(query, sortBy, sortOrder);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Order>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        };
    }

    public async Task<PagedResult<Order>> GetByDateAsync(DateTime date, int? branchId = null, int page = 1, int pageSize = 10, string? sortBy = null, string? sortOrder = "asc")
    {
        return await GetByDateRangeAsync(date.Date, date.Date.AddDays(1).AddTicks(-1), branchId, page, pageSize, sortBy, sortOrder);
    }

    public async Task<List<Order>> GetOrdersInPreparationAsync(int? branchId = null)
    {
        var query = _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.LoyaltyRule)
            .Include(o => o.DeliveryMan)
            .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
            .Where(o => o.Status == OrderStatus.InPreparation)
            .AsQueryable();

        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        return await query
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Order>> GetReadyOrdersAsync(int? branchId = null)
    {
        var query = _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.LoyaltyRule)
            .Include(o => o.DeliveryMan)
            .Where(o => o.Status == OrderStatus.Ready)
            .AsQueryable();

        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        return await query
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Order>> GetOrdersOnTheWayAsync(int? branchId = null)
    {
        var query = _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.LoyaltyRule)
            .Include(o => o.DeliveryMan)
            .Where(o => o.Status == OrderStatus.OnTheWay)
            .AsQueryable();

        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        return await query
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Order>> GetOrdersForDeliveryManAsync(int deliveryManId)
    {
        return await _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.LoyaltyRule)
            .Include(o => o.DeliveryMan)
            .Where(o => o.DeliveryManId == deliveryManId && 
                      (o.Status == OrderStatus.OnTheWay || o.Status == OrderStatus.Ready))
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Order>> GetAvailableOrdersForDeliveryAsync(int? branchId = null)
    {
        var query = _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.LoyaltyRule)
            .Include(o => o.DeliveryMan)
            .Where(o => o.Status == OrderStatus.Ready && o.DeliveryManId == null)
            .AsQueryable();

        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        return await query
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Order>> GetReservationsForDateAsync(DateTime date, int? branchId = null)
    {
        var query = _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Where(o => o.Type == OrderType.Reservation && 
                      o.ReservedFor.HasValue && 
                      o.ReservedFor.Value.Date == date.Date)
            .AsQueryable();

        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        return await query
            .OrderBy(o => o.ReservedFor)
            .ToListAsync();
    }

    public async Task<List<Order>> GetUpcomingReservationsAsync(int? branchId = null, int hours = 24)
    {
        var query = _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Where(o => o.Type == OrderType.Reservation && 
                      o.ReservedFor.HasValue && 
                      o.ReservedFor.Value <= DateTime.UtcNow.AddHours(hours))
            .AsQueryable();

        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        return await query
            .OrderBy(o => o.ReservedFor)
            .ToListAsync();
    }

    public async Task<int> GetTotalOrdersCountAsync(int? branchId = null)
    {
        var query = _context.Orders.AsQueryable();
        
        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        return await query.CountAsync();
    }

    public async Task<int> GetOrdersCountByStatusAsync(OrderStatus status, int? branchId = null)
    {
        var query = _context.Orders.Where(o => o.Status == status);
        
        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        return await query.CountAsync();
    }

    public async Task<int> GetOrdersCountByTypeAsync(OrderType type, int? branchId = null)
    {
        var query = _context.Orders.Where(o => o.Type == type);
        
        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        return await query.CountAsync();
    }

    public async Task<int> GetActiveOrdersCountForDeliveryManAsync(int deliveryManId)
    {
        return await _context.Orders
            .Where(o => o.DeliveryManId == deliveryManId && 
                      (o.Status == OrderStatus.OnTheWay || o.Status == OrderStatus.Ready))
            .CountAsync();
    }

    public async Task<decimal> GetTotalSalesAsync(int? branchId = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var query = _context.Orders.Where(o => o.Status != OrderStatus.Cancelled);
        
        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        if (fromDate.HasValue)
            query = query.Where(o => o.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(o => o.CreatedAt <= toDate.Value);

        return await query.SumAsync(o => o.Total);
    }

    public async Task<decimal> GetAverageOrderValueAsync(int? branchId = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var query = _context.Orders.Where(o => o.Status != OrderStatus.Cancelled);
        
        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        if (fromDate.HasValue)
            query = query.Where(o => o.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(o => o.CreatedAt <= toDate.Value);

        return await query.AverageAsync(o => (decimal?)o.Total) ?? 0;
    }

    public async Task<List<Order>> GetTopSellingProductsAsync(int? branchId = null, DateTime? fromDate = null, DateTime? toDate = null, int limit = 10)
    {
        var query = _context.OrderDetails
            .Include(od => od.Product)
            .Include(od => od.Order)
            .Where(od => od.Order.Status != OrderStatus.Cancelled)
            .AsQueryable();

        if (branchId.HasValue)
            query = query.Where(od => od.Order.BranchId == branchId);

        if (fromDate.HasValue)
            query = query.Where(od => od.Order.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(od => od.Order.CreatedAt <= toDate.Value);

        return await query
            .GroupBy(od => od.ProductId)
            .Select(g => new { ProductId = g.Key, TotalQuantity = g.Sum(od => od.Quantity) })
            .OrderByDescending(x => x.TotalQuantity)
            .Take(limit)
            .Join(_context.Products, x => x.ProductId, p => p.Id, (x, p) => new Order { Id = x.ProductId })
            .ToListAsync();
    }

    public async Task<bool> CanAssignDeliveryManAsync(int orderId, int deliveryManId)
    {
        var order = await _context.Orders.FindAsync(orderId);
        if (order == null || order.Status != OrderStatus.Ready)
            return false;

        // Verificar si el domiciliario ya tiene pedidos en curso
        var activeOrders = await _context.Orders
            .Where(o => o.DeliveryManId == deliveryManId && 
                      (o.Status == OrderStatus.OnTheWay || o.Status == OrderStatus.Ready))
            .CountAsync();

        // Máximo 3 pedidos activos por domiciliario
        return activeOrders < 3;
    }

    public async Task<bool> CanCancelOrderAsync(int orderId)
    {
        var order = await _context.Orders.FindAsync(orderId);
        return order != null && order.Status != OrderStatus.Delivered && order.Status != OrderStatus.Cancelled;
    }

    public async Task<bool> CanChangeStatusAsync(int orderId, OrderStatus newStatus)
    {
        var order = await _context.Orders.FindAsync(orderId);
        if (order == null)
            return false;

        // Lógica de transiciones de estado válidas
        return order.Status switch
        {
            OrderStatus.Taken => newStatus == OrderStatus.InPreparation || newStatus == OrderStatus.Cancelled,
            OrderStatus.InPreparation => newStatus == OrderStatus.Ready || newStatus == OrderStatus.Cancelled,
            OrderStatus.Ready => newStatus == OrderStatus.OnTheWay || newStatus == OrderStatus.Cancelled,
            OrderStatus.OnTheWay => newStatus == OrderStatus.Delivered || newStatus == OrderStatus.Ready,
            OrderStatus.Delivered => false, // No se puede cambiar desde entregado
            OrderStatus.Cancelled => false, // No se puede cambiar desde cancelado
            _ => false
        };
    }

    public async Task<bool> HasActiveOrdersAsync(int customerId)
    {
        return await _context.Orders
            .AnyAsync(o => o.CustomerId == customerId && 
                         o.Status != OrderStatus.Delivered && 
                         o.Status != OrderStatus.Cancelled);
    }

    public async Task<bool> HasOrdersInProgressAsync(int deliveryManId)
    {
        return await _context.Orders
            .AnyAsync(o => o.DeliveryManId == deliveryManId && 
                         (o.Status == OrderStatus.OnTheWay || o.Status == OrderStatus.Ready));
    }

    public async Task<Order> ChangeStatusAsync(int orderId, OrderStatus newStatus, string? reason = null)
    {
        var order = await _context.Orders.FindAsync(orderId);
        if (order == null)
            throw new ArgumentException("Order not found");

        if (!await CanChangeStatusAsync(orderId, newStatus))
            throw new InvalidOperationException($"Cannot change status from {order.Status} to {newStatus}");

        order.Status = newStatus;
        order.AddStatusTime(newStatus, DateTime.UtcNow);

        if (newStatus == OrderStatus.Cancelled && !string.IsNullOrEmpty(reason))
            order.CancelledReason = reason;

        await _context.SaveChangesAsync();
        return order;
    }

    public async Task<Order> AssignDeliveryManAsync(int orderId, int deliveryManId)
    {
        var order = await _context.Orders.FindAsync(orderId);
        if (order == null)
            throw new ArgumentException("Order not found");

        if (!await CanAssignDeliveryManAsync(orderId, deliveryManId))
            throw new InvalidOperationException("Cannot assign delivery man to this order");

        order.DeliveryManId = deliveryManId;
        await _context.SaveChangesAsync();
        return order;
    }

    public async Task<Order> UnassignDeliveryManAsync(int orderId)
    {
        var order = await _context.Orders.FindAsync(orderId);
        if (order == null)
            throw new ArgumentException("Order not found");

        order.DeliveryManId = null;
        await _context.SaveChangesAsync();
        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId, string reason)
    {
        return await ChangeStatusAsync(orderId, OrderStatus.Cancelled, reason);
    }

    public async Task<PagedResult<Order>> SearchOrdersAsync(
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
        string? sortOrder = "asc")
    {
        var query = _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.LoyaltyRule)
            .Include(o => o.DeliveryMan)
            .AsQueryable();

        // Aplicar filtros
        if (!string.IsNullOrEmpty(searchTerm))
        {
            query = query.Where(o => 
                o.Notes!.Contains(searchTerm) ||
                (o.Customer != null && o.Customer.Name.Contains(searchTerm)) ||
                o.Id.ToString().Contains(searchTerm));
        }

        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        if (customerId.HasValue)
            query = query.Where(o => o.CustomerId == customerId);

        if (deliveryManId.HasValue)
            query = query.Where(o => o.DeliveryManId == deliveryManId);

        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);

        if (type.HasValue)
            query = query.Where(o => o.Type == type.Value);

        if (fromDate.HasValue)
            query = query.Where(o => o.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(o => o.CreatedAt <= toDate.Value);

        if (minAmount.HasValue)
            query = query.Where(o => o.Total >= minAmount.Value);

        if (maxAmount.HasValue)
            query = query.Where(o => o.Total <= maxAmount.Value);

        // Aplicar ordenamiento
        query = ApplySorting(query, sortBy, sortOrder);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Order>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        };
    }

    private IQueryable<Order> ApplySorting(IQueryable<Order> query, string? sortBy, string? sortOrder)
    {
        if (string.IsNullOrEmpty(sortBy))
            sortBy = "CreatedAt";

        var isDescending = sortOrder?.ToLower() == "desc";

        return sortBy.ToLower() switch
        {
            "id" => isDescending ? query.OrderByDescending(o => o.Id) : query.OrderBy(o => o.Id),
            "createdat" => isDescending ? query.OrderByDescending(o => o.CreatedAt) : query.OrderBy(o => o.CreatedAt),
            "updatedat" => isDescending ? query.OrderByDescending(o => o.UpdatedAt) : query.OrderBy(o => o.UpdatedAt),
            "status" => isDescending ? query.OrderByDescending(o => o.Status) : query.OrderBy(o => o.Status),
            "type" => isDescending ? query.OrderByDescending(o => o.Type) : query.OrderBy(o => o.Type),
            "total" => isDescending ? query.OrderByDescending(o => o.Total) : query.OrderBy(o => o.Total),
            "subtotal" => isDescending ? query.OrderByDescending(o => o.Subtotal) : query.OrderBy(o => o.Subtotal),
            "reservedfor" => isDescending ? query.OrderByDescending(o => o.ReservedFor) : query.OrderBy(o => o.ReservedFor),
            _ => isDescending ? query.OrderByDescending(o => o.CreatedAt) : query.OrderBy(o => o.CreatedAt)
        };
    }
}
