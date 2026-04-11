using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Models;
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
                .ThenInclude(a => a!.Neighborhood)
            .Include(o => o.LoyaltyCycleStep)
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
                .ThenInclude(a => a!.Neighborhood)
            .Include(o => o.LoyaltyCycleStep)
            .Include(o => o.DeliveryMan)
            .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                    .ThenInclude(p => p.Category)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<Order?> GetByIdWithFullDetailsAsync(int id)
    {
        return await _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
                .ThenInclude(a => a!.Neighborhood)
            .Include(o => o.LoyaltyCycleStep)
            .Include(o => o.DeliveryMan)
            .Include(o => o.DeliveryRoute)
            .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                    .ThenInclude(p => p.Category)
            .Include(o => o.BankPayments)
                .ThenInclude(bp => bp.Bank)
                    .ThenInclude(b => b.Branch)
            .Include(o => o.AppPayments)
                .ThenInclude(ap => ap.App)
                    .ThenInclude(a => a.Bank)
                        .ThenInclude(b => b.Branch)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    /// <summary>
    /// Lista paginada de pedidos. Con rango de fechas completo, filtra por día operativo (creado o <see cref="Order.ReservedFor"/> en el rango UTC).
    /// </summary>
    public async Task<PagedResult<Order>> GetAllAsync(int page, int pageSize, string? sortBy = null, string? sortOrder = "asc", DateTime? fromDate = null, DateTime? toDate = null, int? branchId = null, bool forKitchen = false)
    {
        var query = _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.LoyaltyCycleStep)
            .Include(o => o.DeliveryMan)
            .Include(o => o.BankPayments)
                .ThenInclude(bp => bp.Bank)
                    .ThenInclude(b => b.Branch)
            .Include(o => o.AppPayments)
                .ThenInclude(ap => ap.App)
                    .ThenInclude(a => a.Bank)
                        .ThenInclude(b => b.Branch)
            .AsQueryable();

        // Filtrar por sucursal si se especifica
        if (branchId.HasValue)
        {
            query = query.Where(o => o.BranchId == branchId.Value);
        }

        // Día operativo (misma regla que SearchOrders): creados en el rango UTC o ReservedFor en el rango
        DateTime? rangeFromUtc = null;
        DateTime? rangeToUtc = null;
        if (fromDate.HasValue && toDate.HasValue)
        {
            var fromUtc = fromDate.Value.Kind == DateTimeKind.Utc
                ? fromDate.Value
                : DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Utc);
            var toUtc = toDate.Value.Kind == DateTimeKind.Utc
                ? toDate.Value
                : DateTime.SpecifyKind(toDate.Value, DateTimeKind.Utc);
            rangeFromUtc = fromUtc;
            rangeToUtc = toUtc;
            query = WhereOperationalDateRangeUtc(query, fromUtc, toUtc);
        }
        else if (fromDate.HasValue)
        {
            var fromUtc = fromDate.Value.Kind == DateTimeKind.Utc
                ? fromDate.Value
                : DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Utc);
            query = query.Where(o =>
                o.CreatedAt >= fromUtc
                || (o.ReservedFor.HasValue && o.ReservedFor.Value >= fromUtc));
        }
        else if (toDate.HasValue)
        {
            var toUtc = toDate.Value.Kind == DateTimeKind.Utc
                ? toDate.Value
                : DateTime.SpecifyKind(toDate.Value, DateTimeKind.Utc);
            query = query.Where(o =>
                o.CreatedAt <= toUtc
                || (o.ReservedFor.HasValue && o.ReservedFor.Value <= toUtc));
        }

        if (forKitchen)
        {
            var now = DateTime.UtcNow;
            var colombiaToday = ColombiaTimeHelper.GetNowInColombia().Date;
            var (colombiaTodayStartUtc, colombiaTodayEndUtc) =
                ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(colombiaToday, colombiaToday);

            query = query.Where(o => o.Status == OrderStatus.Taken
                || o.Status == OrderStatus.InPreparation
                || o.Status == OrderStatus.Ready);

            // Hora en que el pedido debe entrar a cocina: prepare_at, o reserved_for - 1h (misma regla que creación).
            // Reservas del día calendario Colombia en Taken: visibles en agenda aunque prepare_at sea más tarde (evita perder noches CO que caen en “mañana” UTC).
            query = query.Where(o =>
                (o.PrepareAt ?? (o.ReservedFor.HasValue ? o.ReservedFor.Value.AddHours(-1) : (DateTime?)null)) == null
                || (o.PrepareAt ?? (o.ReservedFor.HasValue ? o.ReservedFor.Value.AddHours(-1) : (DateTime?)null)) <= now
                || (o.Type == OrderType.Reservation
                    && o.Status == OrderStatus.Taken
                    && (
                        (o.ReservedFor.HasValue
                            && o.ReservedFor.Value >= colombiaTodayStartUtc
                            && o.ReservedFor.Value <= colombiaTodayEndUtc)
                        || (!o.ReservedFor.HasValue
                            && o.PrepareAt.HasValue
                            && o.PrepareAt.Value >= colombiaTodayStartUtc
                            && o.PrepareAt.Value <= colombiaTodayEndUtc))));
        }

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
        
        // Recargar la orden con todas las navegaciones para devolver datos completos
        return await GetByIdAsync(order.Id) ?? order;
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
            .Include(o => o.LoyaltyCycleStep)
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
            .Include(o => o.LoyaltyCycleStep)
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

    public async Task<PagedResult<Order>> GetByStatusAsync(OrderStatus status, OrderType? typeFilter = null, int? branchId = null, int page = 1, int pageSize = 10, string? sortBy = null, string? sortOrder = "asc")
    {
        var query = _context.Orders
            .AsSplitQuery()
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
                .ThenInclude(a => a.Neighborhood)
            .Include(o => o.LoyaltyCycleStep)
            .Include(o => o.DeliveryMan)
            .Include(o => o.BankPayments)
                .ThenInclude(bp => bp.Bank)
                    .ThenInclude(b => b.Branch)
            .Include(o => o.AppPayments)
                .ThenInclude(ap => ap.App)
                    .ThenInclude(a => a.Bank)
                        .ThenInclude(b => b.Branch)
            .Where(o => o.Status == status)
            .AsQueryable();

        if (typeFilter.HasValue)
            query = query.Where(o => o.Type == typeFilter.Value);

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
            .Include(o => o.LoyaltyCycleStep)
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
            .Include(o => o.LoyaltyCycleStep)
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
            .Include(o => o.LoyaltyCycleStep)
            .Include(o => o.DeliveryMan)
            .Where(o =>
                (o.CreatedAt.Date >= fromDate.Date && o.CreatedAt.Date <= toDate.Date)
                || (o.ReservedFor.HasValue
                    && o.ReservedFor.Value.Date >= fromDate.Date
                    && o.ReservedFor.Value.Date <= toDate.Date))
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
            .Include(o => o.LoyaltyCycleStep)
            .Include(o => o.DeliveryMan)
            .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                    .ThenInclude(p => p.Category)
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
            .Include(o => o.LoyaltyCycleStep)
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
                .ThenInclude(a => a!.Neighborhood)
            .Include(o => o.LoyaltyCycleStep)
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
                .ThenInclude(a => a.Neighborhood)
            .Include(o => o.LoyaltyCycleStep)
            .Include(o => o.DeliveryMan)
            .Include(o => o.DeliveryRoute)
            .Where(o => o.DeliveryManId == deliveryManId && 
                      (o.Status == OrderStatus.OnTheWay || o.Status == OrderStatus.Ready) &&
                      o.Type == OrderType.Delivery)
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
                .ThenInclude(a => a.Neighborhood)
            .Include(o => o.LoyaltyCycleStep)
            .Include(o => o.DeliveryMan)
            .Include(o => o.DeliveryRoute)
            .Where(o => o.Status == OrderStatus.Ready && 
                       o.DeliveryManId == null && 
                       o.Type == OrderType.Delivery)
            .AsQueryable();

        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        return await query
            .OrderBy(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Order>> GetReservationsForDateAsync(DateTime date, int? branchId = null)
    {
        var day = date.Date;
        var (fromUtc, toUtc) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(day, day);
        var query = _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Where(o => o.Type == OrderType.Reservation &&
                      o.ReservedFor.HasValue &&
                      o.ReservedFor.Value >= fromUtc &&
                      o.ReservedFor.Value <= toUtc)
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

        if (fromDate.HasValue && toDate.HasValue)
            query = WhereOperationalDateRangeUtc(query, fromDate.Value, toDate.Value);
        else
        {
            if (fromDate.HasValue)
                query = query.Where(o => o.CreatedAt >= fromDate.Value);
            if (toDate.HasValue)
                query = query.Where(o => o.CreatedAt <= toDate.Value);
        }

        return await query.SumAsync(o => o.Total);
    }

    public async Task<decimal> GetAverageOrderValueAsync(int? branchId = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var query = _context.Orders.Where(o => o.Status != OrderStatus.Cancelled);
        
        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        if (fromDate.HasValue && toDate.HasValue)
            query = WhereOperationalDateRangeUtc(query, fromDate.Value, toDate.Value);
        else
        {
            if (fromDate.HasValue)
                query = query.Where(o => o.CreatedAt >= fromDate.Value);
            if (toDate.HasValue)
                query = query.Where(o => o.CreatedAt <= toDate.Value);
        }

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

        if (fromDate.HasValue && toDate.HasValue)
            query = WhereOrderDetailOperationalDateRangeUtc(query, fromDate.Value, toDate.Value);
        else
        {
            if (fromDate.HasValue)
                query = query.Where(od => od.Order.CreatedAt >= fromDate.Value);
            if (toDate.HasValue)
                query = query.Where(od => od.Order.CreatedAt <= toDate.Value);
        }

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
        if (order == null || 
            (order.Status != OrderStatus.Ready && 
             order.Status != OrderStatus.OnTheWay && 
             order.Status != OrderStatus.Delivered))
            return false;

        // No hay límite de pedidos activos por domiciliario
        return true;
    }

    public async Task<bool> CanCancelOrderAsync(int orderId)
    {
        var order = await _context.Orders.FindAsync(orderId);
        return order != null && order.Status != OrderStatus.Cancelled;
    }

    public async Task<bool> CanChangeStatusAsync(int orderId, OrderStatus newStatus)
    {
        var order = await _context.Orders.FindAsync(orderId);
        if (order == null)
            return false;

        if (newStatus == OrderStatus.Cancelled)
            return order.Status != OrderStatus.Cancelled;

        // Transiciones no destructivas (hacia Cancelled arriba)
        return order.Status switch
        {
            OrderStatus.Taken => newStatus == OrderStatus.InPreparation || newStatus == OrderStatus.Cancelled,
            OrderStatus.InPreparation => newStatus == OrderStatus.Ready || newStatus == OrderStatus.Cancelled,
            OrderStatus.Ready => newStatus == OrderStatus.OnTheWay ||
                               newStatus == OrderStatus.Cancelled ||
                               (newStatus == OrderStatus.Delivered && order.Type == OrderType.Onsite),
            OrderStatus.OnTheWay => newStatus == OrderStatus.Delivered || newStatus == OrderStatus.Ready,
            OrderStatus.Delivered => false,
            OrderStatus.Cancelled => false,
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
        
        // Recargar la orden con todas las relaciones para devolver datos completos
        return await GetByIdAsync(orderId) ?? order;
    }

    public async Task<Order> AssignDeliveryManAsync(int orderId, int deliveryManId)
    {
        var order = await _context.Orders.FindAsync(orderId);
        if (order == null)
            throw new NotFoundException("Pedido no encontrado");

        // Validar que el pedido esté en estado Ready, OnTheWay o Delivered
        if (order.Status != OrderStatus.Ready && 
            order.Status != OrderStatus.OnTheWay && 
            order.Status != OrderStatus.Delivered)
            throw new BusinessException($"El pedido debe estar en estado 'Ready', 'OnTheWay' o 'Delivered' para asignar/cambiar domiciliario. Estado actual: {order.Status}");

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
        string? sortOrder = "asc",
        DateTime? reservedFromDate = null,
        DateTime? reservedToDate = null,
        bool excludeFutureReservations = false,
        int? bankId = null,
        int? neighborhoodId = null,
        bool includeOnsiteActiveInAssignedHistory = false)
    {
        // PostgreSQL timestamp with time zone requiere UTC
        if (fromDate.HasValue && fromDate.Value.Kind != DateTimeKind.Utc)
            fromDate = DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Utc);
        if (toDate.HasValue && toDate.Value.Kind != DateTimeKind.Utc)
            toDate = DateTime.SpecifyKind(toDate.Value, DateTimeKind.Utc);

        var query = _context.Orders
            .AsSplitQuery()
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
                .ThenInclude(a => a!.Neighborhood)
            .Include(o => o.LoyaltyCycleStep)
            .Include(o => o.DeliveryMan)
            .Include(o => o.DeliveryRoute)
            .Include(o => o.BankPayments)
                .ThenInclude(bp => bp.Bank)
                    .ThenInclude(b => b.Branch)
            .Include(o => o.AppPayments)
                .ThenInclude(ap => ap.App)
                    .ThenInclude(a => a.Bank)
                        .ThenInclude(b => b.Branch)
            .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                    .ThenInclude(p => p.Category)
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

        if (includeOnsiteActiveInAssignedHistory
            && deliveryManId.HasValue
            && status == OrderStatus.Delivered)
        {
            query = query.Where(o =>
                o.Status == OrderStatus.Delivered
                || (o.Type == OrderType.Onsite && o.Status == OrderStatus.OnTheWay));
        }
        else if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);

        if (type.HasValue)
            query = query.Where(o => o.Type == type.Value);

        // Rango calendario (UTC): pedidos creados en el rango o con ReservedFor en el mismo rango (día operativo)
        if (fromDate.HasValue && toDate.HasValue)
        {
            var fromUtc = fromDate.Value.Kind == DateTimeKind.Utc ? fromDate.Value : DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Utc);
            var toUtc = toDate.Value.Kind == DateTimeKind.Utc ? toDate.Value : DateTime.SpecifyKind(toDate.Value, DateTimeKind.Utc);
            query = query.Where(o =>
                (o.CreatedAt >= fromUtc && o.CreatedAt <= toUtc)
                || (o.ReservedFor.HasValue && o.ReservedFor.Value >= fromUtc && o.ReservedFor.Value <= toUtc));
        }
        else if (fromDate.HasValue)
        {
            var fromUtc = fromDate.Value.Kind == DateTimeKind.Utc ? fromDate.Value : DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Utc);
            query = query.Where(o => o.CreatedAt >= fromUtc);
        }
        else if (toDate.HasValue)
        {
            var toUtc = toDate.Value.Kind == DateTimeKind.Utc ? toDate.Value : DateTime.SpecifyKind(toDate.Value, DateTimeKind.Utc);
            query = query.Where(o => o.CreatedAt <= toUtc);
        }

        if (reservedFromDate.HasValue)
        {
            var rfUtc = reservedFromDate.Value.Kind == DateTimeKind.Utc ? reservedFromDate.Value : DateTime.SpecifyKind(reservedFromDate.Value, DateTimeKind.Utc);
            query = query.Where(o => o.ReservedFor >= rfUtc);
        }

        if (reservedToDate.HasValue)
        {
            var rtUtc = reservedToDate.Value.Kind == DateTimeKind.Utc ? reservedToDate.Value : DateTime.SpecifyKind(reservedToDate.Value, DateTimeKind.Utc);
            query = query.Where(o => o.ReservedFor <= rtUtc);
        }

        if (excludeFutureReservations)
        {
            var startOfTomorrowColombiaUtc = ColombiaTimeHelper.GetColombiaStartOfTomorrowUtc();
            query = query.Where(o =>
                o.Type != OrderType.Reservation ||
                o.ReservedFor == null ||
                o.ReservedFor < startOfTomorrowColombiaUtc);
        }

        if (minAmount.HasValue)
            query = query.Where(o => o.Total >= minAmount.Value);

        if (maxAmount.HasValue)
            query = query.Where(o => o.Total <= maxAmount.Value);

        if (bankId.HasValue)
            query = query.Where(o => o.BankPayments.Any(bp => bp.BankId == bankId.Value));

        if (neighborhoodId.HasValue)
            query = query.Where(o => o.Address != null && o.Address.NeighborhoodId == neighborhoodId.Value);

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

    public async Task<IEnumerable<Order>> GetReservationsDueForPreparation(
        DateTime fromTime, 
        DateTime toTime, 
        OrderStatus status)
    {
        // Usa prepare_at (fallback: reserved_for - 1h). Notificar cuando prepare_at <= now.
        // Excluir ya notificados (prepared_notified_at).
        var now = DateTime.UtcNow;
        return await _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                    .ThenInclude(p => p.Category)
            .Include(o => o.BankPayments)
                .ThenInclude(bp => bp.Bank)
                    .ThenInclude(b => b.Branch)
            .Include(o => o.AppPayments)
                .ThenInclude(ap => ap.App)
                    .ThenInclude(a => a.Bank)
                        .ThenInclude(b => b.Branch)
            .Where(o => o.Status == status
                     && o.PreparedNotifiedAt == null
                     && (o.ReservedFor.HasValue || o.PrepareAt.HasValue)
                     && (
                         (o.PrepareAt.HasValue && o.PrepareAt.Value <= now)
                         || (!o.PrepareAt.HasValue && o.ReservedFor.HasValue && o.ReservedFor.Value.AddHours(-1) <= now)
                     ))
            .ToListAsync();
    }

    public async Task<PrincipalKpiSnapshot> GetPrincipalKpiSnapshotAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var q = WhereOperationalDateRangeUtc(_context.Orders, fromUtc, toUtc);
        if (branchId.HasValue)
            q = q.Where(o => o.BranchId == branchId.Value);

        var totalAll = await q.CountAsync(cancellationToken);
        var cancelledCount = await q.CountAsync(o => o.Status == OrderStatus.Cancelled, cancellationToken);
        var nonCancelled = q.Where(o => o.Status != OrderStatus.Cancelled);

        var completedCount = await nonCancelled.CountAsync(cancellationToken);
        var totalSales = completedCount > 0
            ? await nonCancelled.SumAsync(o => (long)o.Total, cancellationToken)
            : 0L;

        var avgTicket = completedCount > 0
            ? (int)Math.Round((double)totalSales / completedCount)
            : 0;

        var cancelRate = totalAll > 0
            ? Math.Round(cancelledCount * 100.0 / totalAll, 4)
            : 0d;

        return new PrincipalKpiSnapshot((decimal)totalSales, completedCount, avgTicket, cancelRate);
    }

    public async Task<PrincipalPipelineCounts> GetPrincipalPipelineCountsAsync(
        int? branchId,
        CancellationToken cancellationToken = default)
    {
        var statuses = new[]
        {
            OrderStatus.Taken,
            OrderStatus.InPreparation,
            OrderStatus.Ready,
            OrderStatus.OnTheWay,
        };

        var q = _context.Orders.Where(o => statuses.Contains(o.Status));
        if (branchId.HasValue)
            q = q.Where(o => o.BranchId == branchId.Value);

        var groups = await q
            .GroupBy(o => o.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var dict = groups.ToDictionary(x => x.Key, x => x.Count);

        int Get(OrderStatus s) => dict.TryGetValue(s, out var c) ? c : 0;

        return new PrincipalPipelineCounts(
            Get(OrderStatus.Taken),
            Get(OrderStatus.InPreparation),
            Get(OrderStatus.Ready),
            Get(OrderStatus.OnTheWay));
    }

    public async Task<List<Order>> GetRecentOrdersForDashboardAsync(
        int? branchId,
        int take,
        CancellationToken cancellationToken = default)
    {
        var q = _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.Customer)
            .AsQueryable();

        if (branchId.HasValue)
            q = q.Where(o => o.BranchId == branchId.Value);

        return await q
            .OrderByDescending(o => o.UpdatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Order>> GetDeliveredDeliveryOrdersForDashboardAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        int? deliveryManId = null,
        CancellationToken cancellationToken = default)
    {
        var q = _context.Orders
            .AsNoTracking()
            .Include(o => o.DeliveryMan)
            .Where(o =>
                o.Type == OrderType.Delivery
                && o.Status == OrderStatus.Delivered
                && o.UpdatedAt >= fromUtc
                && o.UpdatedAt <= toUtc);

        if (branchId.HasValue)
            q = q.Where(o => o.BranchId == branchId.Value);
        if (deliveryManId.HasValue)
            q = q.Where(o => o.DeliveryManId == deliveryManId.Value);

        return await q.ToListAsync(cancellationToken);
    }

    public async Task<List<(DateTime UpdatedAt, int Total)>> GetDeliveredOrdersSalesTicksForDashboardAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        int? deliveryManId = null,
        CancellationToken cancellationToken = default)
    {
        var q = _context.Orders
            .AsNoTracking()
            .Where(o =>
                o.Status == OrderStatus.Delivered
                && o.UpdatedAt >= fromUtc
                && o.UpdatedAt <= toUtc);

        if (branchId.HasValue)
            q = q.Where(o => o.BranchId == branchId.Value);
        if (deliveryManId.HasValue)
        {
            q = q.Where(o =>
                o.Type == OrderType.Delivery
                && o.DeliveryManId == deliveryManId.Value);
        }

        return await q
            .Select(o => new ValueTuple<DateTime, int>(o.UpdatedAt, o.Total))
            .ToListAsync(cancellationToken);
    }

    private IQueryable<Order> DashboardNonCancelledOrdersInRange(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var q = WhereOperationalDateRangeUtc(
            _context.Orders.AsNoTracking().Where(o => o.Status != OrderStatus.Cancelled),
            fromUtc,
            toUtc);

        if (branchId.HasValue)
            q = q.Where(o => o.BranchId == branchId.Value);

        return q;
    }

    public async Task<List<BranchSalesComparisonAggregate>> GetDashboardSalesComparisonAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        return await DashboardNonCancelledOrdersInRange(branchId, fromUtc, toUtc)
            .GroupBy(o => o.BranchId)
            .Select(g => new BranchSalesComparisonAggregate
            {
                BranchId = g.Key,
                SalesTotal = g.Sum(o => o.Total),
                OrdersTotal = g.Count(),
                SalesDelivery = g.Sum(o => o.Type == OrderType.Delivery ? o.Total : 0),
                SalesOnsite = g.Sum(o => o.Type != OrderType.Delivery ? o.Total : 0),
                OrdersDelivery = g.Count(o => o.Type == OrderType.Delivery),
                OrdersOnsite = g.Count(o => o.Type != OrderType.Delivery),
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<SalesDayPoint>> GetDashboardSalesByDayAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var rows = await DashboardNonCancelledOrdersInRange(branchId, fromUtc, toUtc)
            .Select(o => new { o.BranchId, o.CreatedAt, o.ReservedFor, o.Total })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(o => new
            {
                o.BranchId,
                Day = ColombiaTimeHelper.OrderOperationalColombiaCalendarDate(o.CreatedAt, o.ReservedFor),
            })
            .Select(g => new SalesDayPoint(g.Key.BranchId, g.Key.Day, g.Sum(x => x.Total)))
            .ToList();
    }

    public async Task<List<OrdersDayPoint>> GetDashboardOrdersByDayAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var rows = await DashboardNonCancelledOrdersInRange(branchId, fromUtc, toUtc)
            .Select(o => new { o.CreatedAt, o.ReservedFor })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(o => ColombiaTimeHelper.OrderOperationalColombiaCalendarDate(o.CreatedAt, o.ReservedFor))
            .Select(g => new OrdersDayPoint(g.Key, g.Count()))
            .ToList();
    }

    public async Task<List<SalesMonthPoint>> GetDashboardSalesByMonthAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var rows = await DashboardNonCancelledOrdersInRange(branchId, fromUtc, toUtc)
            .Select(o => new { o.BranchId, o.CreatedAt, o.ReservedFor, o.Total })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(o => new
            {
                o.BranchId,
                Ym = ColombiaTimeHelper.OrderOperationalColombiaYearMonth(o.CreatedAt, o.ReservedFor),
            })
            .Select(g => new SalesMonthPoint(
                g.Key.BranchId,
                g.Key.Ym.Year,
                g.Key.Ym.Month,
                g.Sum(x => x.Total)))
            .ToList();
    }

    public async Task<List<OrdersMonthPoint>> GetDashboardOrdersByMonthAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var rows = await DashboardNonCancelledOrdersInRange(branchId, fromUtc, toUtc)
            .Select(o => new { o.CreatedAt, o.ReservedFor })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(o => ColombiaTimeHelper.OrderOperationalColombiaYearMonth(o.CreatedAt, o.ReservedFor))
            .Select(g => new OrdersMonthPoint(g.Key.Year, g.Key.Month, g.Count()))
            .ToList();
    }

    public async Task<List<SalesYearPoint>> GetDashboardSalesByYearAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var rows = await DashboardNonCancelledOrdersInRange(branchId, fromUtc, toUtc)
            .Select(o => new { o.BranchId, o.CreatedAt, o.ReservedFor, o.Total })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(o => new
            {
                o.BranchId,
                Year = ColombiaTimeHelper.OrderOperationalColombiaYear(o.CreatedAt, o.ReservedFor),
            })
            .Select(g => new SalesYearPoint(g.Key.BranchId, g.Key.Year, g.Sum(x => x.Total)))
            .ToList();
    }

    public async Task<List<OrdersYearPoint>> GetDashboardOrdersByYearAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var rows = await DashboardNonCancelledOrdersInRange(branchId, fromUtc, toUtc)
            .Select(o => new { o.CreatedAt, o.ReservedFor })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(o => ColombiaTimeHelper.OrderOperationalColombiaYear(o.CreatedAt, o.ReservedFor))
            .Select(g => new OrdersYearPoint(g.Key, g.Count()))
            .ToList();
    }

    public async Task<List<SalesHourPoint>> GetDashboardSalesByHourAsync(
        int? branchId,
        DateTime dayStartUtc,
        DateTime dayEndUtc,
        CancellationToken cancellationToken = default)
    {
        var rows = await DashboardNonCancelledOrdersInRange(branchId, dayStartUtc, dayEndUtc)
            .Select(o => new { o.BranchId, o.CreatedAt, o.ReservedFor, o.Total })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(o => new
            {
                o.BranchId,
                Hour = ColombiaTimeHelper.OrderOperationalColombiaHour(o.CreatedAt, o.ReservedFor),
            })
            .Select(g => new SalesHourPoint(g.Key.BranchId, g.Key.Hour, g.Sum(x => x.Total)))
            .ToList();
    }

    public async Task<List<OrdersHourPoint>> GetDashboardOrdersByHourAsync(
        int? branchId,
        DateTime dayStartUtc,
        DateTime dayEndUtc,
        CancellationToken cancellationToken = default)
    {
        var rows = await DashboardNonCancelledOrdersInRange(branchId, dayStartUtc, dayEndUtc)
            .Select(o => new { o.CreatedAt, o.ReservedFor })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(o => ColombiaTimeHelper.OrderOperationalColombiaHour(o.CreatedAt, o.ReservedFor))
            .Select(g => new OrdersHourPoint(g.Key, g.Count()))
            .ToList();
    }

    public async Task<List<SalesProductAggregateRow>> GetSalesProductAggregatesForDashboardAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var q = WhereOrderDetailOperationalDateRangeUtc(
            _context.OrderDetails
                .AsNoTracking()
                .Where(od => od.Order.Status != OrderStatus.Cancelled),
            fromUtc,
            toUtc);

        if (branchId.HasValue)
            q = q.Where(od => od.Order.BranchId == branchId.Value);

        return await q
            .GroupBy(od => new { od.ProductId, Name = od.Product.Name ?? string.Empty })
            .Select(g => new SalesProductAggregateRow
            {
                ProductId = g.Key.ProductId,
                ProductName = g.Key.Name,
                QuantitySold = g.Sum(od => od.Quantity),
                RevenueCop = g.Sum(od => (long)(od.Subtotal ?? (od.Quantity * od.UnitPrice - od.Discount))),
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<SalesProductCategoryAggregateRow>> GetSalesProductCategoryAggregatesForDashboardAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var q = WhereOrderDetailOperationalDateRangeUtc(
            _context.OrderDetails
                .AsNoTracking()
                .Where(od => od.Order.Status != OrderStatus.Cancelled),
            fromUtc,
            toUtc);

        if (branchId.HasValue)
            q = q.Where(od => od.Order.BranchId == branchId.Value);

        return await q
            .GroupBy(od => new
            {
                od.ProductId,
                ProductName = od.Product.Name ?? string.Empty,
                od.Product.CategoryId,
                CategoryName = od.Product.Category.Name ?? string.Empty,
            })
            .Select(g => new SalesProductCategoryAggregateRow
            {
                ProductId = g.Key.ProductId,
                ProductName = g.Key.ProductName,
                CategoryId = g.Key.CategoryId,
                CategoryName = g.Key.CategoryName,
                QuantitySold = g.Sum(od => od.Quantity),
                RevenueCop = g.Sum(od => (long)(od.Subtotal ?? (od.Quantity * od.UnitPrice - od.Discount))),
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<SalesCategoryAggregateRow>> GetSalesCategoryAggregatesForDashboardAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var q = WhereOrderDetailOperationalDateRangeUtc(
            _context.OrderDetails
                .AsNoTracking()
                .Where(od => od.Order.Status != OrderStatus.Cancelled),
            fromUtc,
            toUtc);

        if (branchId.HasValue)
            q = q.Where(od => od.Order.BranchId == branchId.Value);

        return await q
            .GroupBy(od => new
            {
                od.Product.CategoryId,
                Name = od.Product.Category.Name ?? string.Empty,
            })
            .Select(g => new SalesCategoryAggregateRow
            {
                CategoryId = g.Key.CategoryId,
                CategoryName = g.Key.Name,
                QuantitySold = g.Sum(od => od.Quantity),
                RevenueCop = g.Sum(od => (long)(od.Subtotal ?? (od.Quantity * od.UnitPrice - od.Discount))),
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<SalesCategoryWeightRow>> GetSalesCategoryWeightAggregatesForDashboardAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var q = WhereOrderDetailOperationalDateRangeUtc(
            _context.OrderDetails
                .AsNoTracking()
                .Where(od => od.Order.Status != OrderStatus.Cancelled && od.Product.WeightGrams != null),
            fromUtc,
            toUtc);

        if (branchId.HasValue)
            q = q.Where(od => od.Order.BranchId == branchId.Value);

        var rows = await q
            .GroupBy(od => new
            {
                od.Product.CategoryId,
                Name = od.Product.Category.Name ?? string.Empty,
            })
            .Select(g => new SalesCategoryWeightRow
            {
                CategoryId = g.Key.CategoryId,
                CategoryName = g.Key.Name,
                TotalWeightGrams = g.Sum(od => (long)od.Quantity * od.Product.WeightGrams!.Value),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(r => r.TotalWeightGrams > 0)
            .OrderByDescending(r => r.TotalWeightGrams)
            .ThenBy(r => r.CategoryName)
            .ToList();
    }

    public async Task<List<SalesProductWeightRow>> GetSalesProductWeightAggregatesForDashboardAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var q = WhereOrderDetailOperationalDateRangeUtc(
            _context.OrderDetails
                .AsNoTracking()
                .Where(od => od.Order.Status != OrderStatus.Cancelled && od.Product.WeightGrams != null),
            fromUtc,
            toUtc);

        if (branchId.HasValue)
            q = q.Where(od => od.Order.BranchId == branchId.Value);

        var rows = await q
            .GroupBy(od => new { od.ProductId, Name = od.Product.Name ?? string.Empty })
            .Select(g => new SalesProductWeightRow
            {
                ProductId = g.Key.ProductId,
                ProductName = g.Key.Name,
                TotalWeightGrams = g.Sum(od => (long)od.Quantity * od.Product.WeightGrams!.Value),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Where(r => r.TotalWeightGrams > 0)
            .OrderByDescending(r => r.TotalWeightGrams)
            .ThenBy(r => r.ProductName)
            .ToList();
    }

    public async Task<List<SalesCategoryWeightEvolutionPoint>> GetSalesCategoryWeightEvolutionAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        int categoryId,
        CategoryWeightEvolutionGranularity granularity,
        CancellationToken cancellationToken = default)
    {
        var q = WhereOrderDetailOperationalDateRangeUtc(
            _context.OrderDetails
                .AsNoTracking()
                .Where(od =>
                    od.Order.Status != OrderStatus.Cancelled
                    && od.Product.WeightGrams != null
                    && od.Product.CategoryId == categoryId),
            fromUtc,
            toUtc);

        if (branchId.HasValue)
            q = q.Where(od => od.Order.BranchId == branchId.Value);

        // Proyectar solo tipos anónimos traducibles; el record en Select hace que EF genere SQL inválido.
        return granularity switch
        {
            CategoryWeightEvolutionGranularity.Day => await EvolutionByDayAsync(q, cancellationToken),
            CategoryWeightEvolutionGranularity.Month => await EvolutionByMonthAsync(q, cancellationToken),
            CategoryWeightEvolutionGranularity.Year => await EvolutionByYearAsync(q, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, null),
        };
    }

    public async Task<List<SalesCategoryWeightEvolutionSeries>> GetSalesCategoryWeightEvolutionAllCategoriesAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CategoryWeightEvolutionGranularity granularity,
        CancellationToken cancellationToken = default)
    {
        var q = WhereOrderDetailOperationalDateRangeUtc(
            _context.OrderDetails
                .AsNoTracking()
                .Where(od =>
                    od.Order.Status != OrderStatus.Cancelled && od.Product.WeightGrams != null),
            fromUtc,
            toUtc);

        if (branchId.HasValue)
            q = q.Where(od => od.Order.BranchId == branchId.Value);

        return granularity switch
        {
            CategoryWeightEvolutionGranularity.Day => await EvolutionAllCategoriesByDayAsync(q, cancellationToken),
            CategoryWeightEvolutionGranularity.Month => await EvolutionAllCategoriesByMonthAsync(q, cancellationToken),
            CategoryWeightEvolutionGranularity.Year => await EvolutionAllCategoriesByYearAsync(q, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, null),
        };
    }

    private static async Task<List<SalesCategoryWeightEvolutionPoint>> EvolutionByDayAsync(
        IQueryable<OrderDetail> q,
        CancellationToken cancellationToken)
    {
        var raw = await q
            .Select(od => new
            {
                od.Order.CreatedAt,
                od.Order.ReservedFor,
                W = (long)od.Quantity * od.Product.WeightGrams!.Value,
            })
            .ToListAsync(cancellationToken);

        return raw
            .GroupBy(x => ColombiaTimeHelper.OrderOperationalColombiaCalendarDate(x.CreatedAt, x.ReservedFor))
            .Select(g => new SalesCategoryWeightEvolutionPoint(
                ColombiaTimeHelper.ColombiaCalendarDayStartUtc(g.Key),
                g.Sum(x => x.W)))
            .OrderBy(p => p.BucketStartUtc)
            .ToList();
    }

    private static async Task<List<SalesCategoryWeightEvolutionPoint>> EvolutionByMonthAsync(
        IQueryable<OrderDetail> q,
        CancellationToken cancellationToken)
    {
        var raw = await q
            .Select(od => new
            {
                od.Order.CreatedAt,
                od.Order.ReservedFor,
                W = (long)od.Quantity * od.Product.WeightGrams!.Value,
            })
            .ToListAsync(cancellationToken);

        return raw
            .GroupBy(x => ColombiaTimeHelper.OrderOperationalColombiaYearMonth(x.CreatedAt, x.ReservedFor))
            .Select(g => new SalesCategoryWeightEvolutionPoint(
                ColombiaTimeHelper.ColombiaCalendarDayStartUtc(new DateTime(g.Key.Year, g.Key.Month, 1)),
                g.Sum(x => x.W)))
            .OrderBy(p => p.BucketStartUtc)
            .ToList();
    }

    private static async Task<List<SalesCategoryWeightEvolutionPoint>> EvolutionByYearAsync(
        IQueryable<OrderDetail> q,
        CancellationToken cancellationToken)
    {
        var raw = await q
            .Select(od => new
            {
                od.Order.CreatedAt,
                od.Order.ReservedFor,
                W = (long)od.Quantity * od.Product.WeightGrams!.Value,
            })
            .ToListAsync(cancellationToken);

        return raw
            .GroupBy(x => ColombiaTimeHelper.OrderOperationalColombiaYear(x.CreatedAt, x.ReservedFor))
            .Select(g => new SalesCategoryWeightEvolutionPoint(
                ColombiaTimeHelper.ColombiaCalendarDayStartUtc(new DateTime(g.Key, 1, 1)),
                g.Sum(x => x.W)))
            .OrderBy(p => p.BucketStartUtc)
            .ToList();
    }

    private static async Task<List<SalesCategoryWeightEvolutionSeries>> EvolutionAllCategoriesByDayAsync(
        IQueryable<OrderDetail> q,
        CancellationToken cancellationToken)
    {
        var raw = await q
            .Select(od => new
            {
                od.Product.CategoryId,
                Name = od.Product.Category.Name ?? string.Empty,
                od.Order.CreatedAt,
                od.Order.ReservedFor,
                W = (long)od.Quantity * od.Product.WeightGrams!.Value,
            })
            .ToListAsync(cancellationToken);

        return raw
            .GroupBy(x => (x.CategoryId, x.Name))
            .Select(g => new SalesCategoryWeightEvolutionSeries(
                g.Key.CategoryId,
                g.Key.Name,
                g.GroupBy(x => ColombiaTimeHelper.OrderOperationalColombiaCalendarDate(x.CreatedAt, x.ReservedFor))
                    .Select(gg => new SalesCategoryWeightEvolutionPoint(
                        ColombiaTimeHelper.ColombiaCalendarDayStartUtc(gg.Key),
                        gg.Sum(x => x.W)))
                    .OrderBy(p => p.BucketStartUtc)
                    .ToList()))
            .OrderBy(s => s.CategoryName)
            .ToList();
    }

    private static async Task<List<SalesCategoryWeightEvolutionSeries>> EvolutionAllCategoriesByMonthAsync(
        IQueryable<OrderDetail> q,
        CancellationToken cancellationToken)
    {
        var raw = await q
            .Select(od => new
            {
                od.Product.CategoryId,
                Name = od.Product.Category.Name ?? string.Empty,
                od.Order.CreatedAt,
                od.Order.ReservedFor,
                W = (long)od.Quantity * od.Product.WeightGrams!.Value,
            })
            .ToListAsync(cancellationToken);

        return raw
            .GroupBy(x => (x.CategoryId, x.Name))
            .Select(g => new SalesCategoryWeightEvolutionSeries(
                g.Key.CategoryId,
                g.Key.Name,
                g.GroupBy(x => ColombiaTimeHelper.OrderOperationalColombiaYearMonth(x.CreatedAt, x.ReservedFor))
                    .Select(gg => new SalesCategoryWeightEvolutionPoint(
                        ColombiaTimeHelper.ColombiaCalendarDayStartUtc(new DateTime(gg.Key.Year, gg.Key.Month, 1)),
                        gg.Sum(x => x.W)))
                    .OrderBy(p => p.BucketStartUtc)
                    .ToList()))
            .OrderBy(s => s.CategoryName)
            .ToList();
    }

    private static async Task<List<SalesCategoryWeightEvolutionSeries>> EvolutionAllCategoriesByYearAsync(
        IQueryable<OrderDetail> q,
        CancellationToken cancellationToken)
    {
        var raw = await q
            .Select(od => new
            {
                od.Product.CategoryId,
                Name = od.Product.Category.Name ?? string.Empty,
                od.Order.CreatedAt,
                od.Order.ReservedFor,
                W = (long)od.Quantity * od.Product.WeightGrams!.Value,
            })
            .ToListAsync(cancellationToken);

        return raw
            .GroupBy(x => (x.CategoryId, x.Name))
            .Select(g => new SalesCategoryWeightEvolutionSeries(
                g.Key.CategoryId,
                g.Key.Name,
                g.GroupBy(x => ColombiaTimeHelper.OrderOperationalColombiaYear(x.CreatedAt, x.ReservedFor))
                    .Select(gg => new SalesCategoryWeightEvolutionPoint(
                        ColombiaTimeHelper.ColombiaCalendarDayStartUtc(new DateTime(gg.Key, 1, 1)),
                        gg.Sum(x => x.W)))
                    .OrderBy(p => p.BucketStartUtc)
                    .ToList()))
            .OrderBy(s => s.CategoryName)
            .ToList();
    }

    public async Task<int> CountDeliveredOrdersForCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return await _context.Orders.CountAsync(
            o => o.CustomerId == customerId && o.Status == OrderStatus.Delivered,
            cancellationToken);
    }

    public async Task UpdateOrderLoyaltyCycleAsync(
        int orderId,
        int? loyaltyCycleStepId,
        string? loyaltyRewardSnapshot,
        CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders.FindAsync(new object[] { orderId }, cancellationToken);
        if (order == null)
            return;

        order.LoyaltyCycleStepId = loyaltyCycleStepId;
        order.LoyaltyRewardSnapshot = loyaltyRewardSnapshot;
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Incluye pedidos creados en el rango UTC o con <see cref="Order.ReservedFor"/> en el mismo rango (día operativo Colombia → UTC en el handler).
    /// </summary>
    private static IQueryable<Order> WhereOperationalDateRangeUtc(
        IQueryable<Order> orders,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var from = fromUtc.Kind == DateTimeKind.Utc ? fromUtc : DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        var to = toUtc.Kind == DateTimeKind.Utc ? toUtc : DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);
        return orders.Where(o =>
            (o.CreatedAt >= from && o.CreatedAt <= to)
            || (o.ReservedFor.HasValue && o.ReservedFor.Value >= from && o.ReservedFor.Value <= to));
    }

    private static IQueryable<OrderDetail> WhereOrderDetailOperationalDateRangeUtc(
        IQueryable<OrderDetail> query,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var from = fromUtc.Kind == DateTimeKind.Utc ? fromUtc : DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        var to = toUtc.Kind == DateTimeKind.Utc ? toUtc : DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);
        return query.Where(od =>
            (od.Order.CreatedAt >= from && od.Order.CreatedAt <= to)
            || (od.Order.ReservedFor.HasValue && od.Order.ReservedFor.Value >= from && od.Order.ReservedFor.Value <= to));
    }
}
