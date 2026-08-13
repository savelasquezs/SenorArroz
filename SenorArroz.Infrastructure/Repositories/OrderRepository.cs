using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Models;
using SenorArroz.Infrastructure.Common;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Infrastructure.Repositories;

public class OrderRepository : IOrderRepository
{
    private sealed class DeliveredAtRangeCountRow
    {
        public long Total { get; set; }
    }

    private sealed class DeliveredAtRangeIdRow
    {
        public int Id { get; set; }
    }

    private readonly ApplicationDbContext _context;
    private readonly IClock _clock;

    public OrderRepository(ApplicationDbContext context, IClock clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<Order?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .AsNoTracking()
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
                .ThenInclude(a => a!.Neighborhood)
            .Include(o => o.LoyaltyCycleStep)
            .Include(o => o.DeliveryMan)
            .Include(o => o.DeliveryRouteStop)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<Order?> GetByIdWithDetailsAsync(int id, CancellationToken cancellationToken = default)
    {
        // IdentityResolution: varias líneas pueden compartir Product/Category; sin esto Update() falla al rastrear duplicados.
        return await _context.Orders
            .AsNoTrackingWithIdentityResolution()
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
                .ThenInclude(a => a!.Neighborhood)
            .Include(o => o.LoyaltyCycleStep)
            .Include(o => o.DeliveryMan)
            .Include(o => o.DeliveryRouteStop)
            .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                    .ThenInclude(p => p.Category)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<Order?> GetByIdWithFullDetailsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .AsNoTrackingWithIdentityResolution()
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
                .ThenInclude(a => a!.Neighborhood)
            .Include(o => o.LoyaltyCycleStep)
            .Include(o => o.DeliveryMan)
            .Include(o => o.DeliveryRoute)
            .Include(o => o.DeliveryRouteStop)
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
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    /// <summary>
    /// Lista paginada de pedidos. Con rango de fechas completo, filtra por día operativo (creado o <see cref="Order.ReservedFor"/> en el rango UTC).
    /// </summary>
    public async Task<PagedResult<Order>> GetAllAsync(int page, int pageSize, string? sortBy = null, string? sortOrder = "asc", DateTime? fromDate = null, DateTime? toDate = null, int? branchId = null, bool forKitchen = false, CancellationToken cancellationToken = default)
    {
        var query = _context.Orders
            .AsNoTracking()
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.LoyaltyCycleStep)
            .Include(o => o.DeliveryMan)
            .Include(o => o.DeliveryRouteStop)
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
            var now = _clock.UtcNow;
            var colombiaToday = ColombiaTimeHelper.GetNowInColombiaFromUtc(now).Date;
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

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<Order> CreateAsync(Order order, CancellationToken cancellationToken = default)
    {
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);
        return order;
    }

    public async Task<Order> UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            OrderUpdateGraphForPersistence.DetachReadOnlyNavigations(order);

            // Pedido cargado con AsNoTracking y mutado en memoria: DbSet.Update() no emite DELETE para líneas
            // quitadas del ICollection (dejan de ser alcanzables en el grafo). Borrar en BD las que ya no estén en el payload.
            if (order.OrderDetails != null)
            {
                var keepIds = order.OrderDetails.Where(d => d.Id > 0).Select(d => d.Id).ToHashSet();
                await _context.OrderDetails
                    .Where(od => od.OrderId == order.Id && !keepIds.Contains(od.Id))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            _context.Orders.Update(order);
            await _context.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return await GetByIdAsync(order.Id, cancellationToken) ?? order;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders.FindAsync([id], cancellationToken);
        if (order != null)
        {
            _context.Orders.Remove(order);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<PagedResult<Order>> GetByBranchAsync(int branchId, int page, int pageSize, string? sortBy = null, string? sortOrder = "asc", CancellationToken cancellationToken = default)
    {
        var query = _context.Orders
            .AsNoTracking()
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.LoyaltyCycleStep)
            .Include(o => o.DeliveryMan)
            .Where(o => o.BranchId == branchId)
            .AsQueryable();

        query = ApplySorting(query, sortBy, sortOrder);

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<PagedResult<Order>> GetByCustomerAsync(int customerId, int page, int pageSize, string? sortBy = null, string? sortOrder = "asc", CancellationToken cancellationToken = default)
    {
        var query = _context.Orders
            .AsNoTracking()
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.LoyaltyCycleStep)
            .Include(o => o.DeliveryMan)
            .Where(o => o.CustomerId == customerId)
            .AsQueryable();

        query = ApplySorting(query, sortBy, sortOrder);

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<PagedResult<Order>> GetByStatusAsync(OrderStatus status, OrderType? typeFilter = null, int? branchId = null, int page = 1, int pageSize = 10, string? sortBy = null, string? sortOrder = "asc", CancellationToken cancellationToken = default)
    {
        var query = _context.Orders
            .AsNoTracking()
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

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<PagedResult<Order>> GetByTypeAsync(OrderType type, int? branchId = null, int page = 1, int pageSize = 10, string? sortBy = null, string? sortOrder = "asc", CancellationToken cancellationToken = default)
    {
        var query = _context.Orders
            .AsNoTracking()
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

        return await query.ToPagedResultAsync(page, pageSize);
    }

    public async Task<PagedResult<Order>> GetByDeliveryManAsync(int deliveryManId, int page, int pageSize, string? sortBy = null, string? sortOrder = "asc", CancellationToken cancellationToken = default)
    {
        var query = _context.Orders
            .AsNoTracking()
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.LoyaltyCycleStep)
            .Include(o => o.DeliveryMan)
            .Where(o => o.DeliveryManId == deliveryManId)
            .AsQueryable();

        query = ApplySorting(query, sortBy, sortOrder);

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<PagedResult<Order>> GetByDateRangeAsync(DateTime fromDate, DateTime toDate, int? branchId = null, int page = 1, int pageSize = 10, string? sortBy = null, string? sortOrder = "asc", CancellationToken cancellationToken = default)
    {
        var (rangeFromUtc, rangeToUtc) =
            ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(fromDate.Date, toDate.Date);

        var query = _context.Orders
            .AsNoTracking()
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Include(o => o.LoyaltyCycleStep)
            .Include(o => o.DeliveryMan)
            .Where(o =>
                (o.CreatedAt >= rangeFromUtc && o.CreatedAt <= rangeToUtc)
                || (o.ReservedFor.HasValue
                    && o.ReservedFor.Value >= rangeFromUtc
                    && o.ReservedFor.Value <= rangeToUtc))
            .AsQueryable();

        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        query = ApplySorting(query, sortBy, sortOrder);

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<PagedResult<Order>> GetByDateAsync(DateTime date, int? branchId = null, int page = 1, int pageSize = 10, string? sortBy = null, string? sortOrder = "asc", CancellationToken cancellationToken = default)
    {
        return await GetByDateRangeAsync(date.Date, date.Date, branchId, page, pageSize, sortBy, sortOrder, cancellationToken);
    }

    public async Task<List<Order>> GetOrdersInPreparationAsync(int? branchId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Orders
            .AsNoTracking()
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
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Order>> GetReadyOrdersAsync(int? branchId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Orders
            .AsNoTracking()
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
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Order>> GetOrdersOnTheWayAsync(int? branchId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Orders
            .AsNoTracking()
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
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Order>> GetOrdersForDeliveryManAsync(int deliveryManId, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .AsNoTracking()
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
                      o.Type == OrderType.Delivery &&
                      o.ExternalFulfillmentProvider == null)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Order>> GetAvailableOrdersForDeliveryAsync(int? branchId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Orders
            .AsNoTracking()
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
                       o.Type == OrderType.Delivery &&
                       o.ExternalFulfillmentProvider == null)
            .AsQueryable();

        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        return await query
            .OrderBy(o => o.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Order>> GetReservationsForDateAsync(DateTime date, int? branchId = null, CancellationToken cancellationToken = default)
    {
        var day = date.Date;
        var (fromUtc, toUtc) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(day, day);
        var query = _context.Orders
            .AsNoTracking()
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
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Order>> GetUpcomingReservationsAsync(int? branchId = null, int hours = 24, CancellationToken cancellationToken = default)
    {
        var query = _context.Orders
            .AsNoTracking()
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
            .Where(o => o.Type == OrderType.Reservation &&
                      o.ReservedFor.HasValue &&
                      o.ReservedFor.Value <= _clock.UtcNow.AddHours(hours))
            .AsQueryable();

        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        return await query
            .OrderBy(o => o.ReservedFor)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetTotalOrdersCountAsync(int? branchId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Orders.AsQueryable();

        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        return await query.CountAsync(cancellationToken);
    }

    public async Task<int> GetOrdersCountByStatusAsync(OrderStatus status, int? branchId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Orders.Where(o => o.Status == status);

        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        return await query.CountAsync(cancellationToken);
    }

    public async Task<int> GetOrdersCountByTypeAsync(OrderType type, int? branchId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Orders.Where(o => o.Type == type);

        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        return await query.CountAsync(cancellationToken);
    }

    public async Task<int> GetActiveOrdersCountForDeliveryManAsync(int deliveryManId, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Where(o => o.DeliveryManId == deliveryManId &&
                      (o.Status == OrderStatus.OnTheWay || o.Status == OrderStatus.Ready))
            .CountAsync(cancellationToken);
    }

    public async Task<decimal> GetTotalSalesAsync(int? branchId = null, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Orders.Where(o => o.Status != OrderStatus.Cancelled);
        
        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        if (fromDate.HasValue && toDate.HasValue)
            query = ApplyOrderSalesDateRange(query, fromDate.Value, toDate.Value);
        else
        {
            if (fromDate.HasValue)
                query = query.Where(o => (o.PrepareAt ?? o.CreatedAt) >= fromDate.Value);
            if (toDate.HasValue)
                query = query.Where(o => (o.PrepareAt ?? o.CreatedAt) <= toDate.Value);
        }

        return await query.SumAsync(o => o.Total, cancellationToken);
    }

    public async Task<decimal> GetAverageOrderValueAsync(int? branchId = null, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Orders.Where(o => o.Status != OrderStatus.Cancelled);

        if (branchId.HasValue)
            query = query.Where(o => o.BranchId == branchId);

        if (fromDate.HasValue && toDate.HasValue)
            query = ApplyOrderSalesDateRange(query, fromDate.Value, toDate.Value);
        else
        {
            if (fromDate.HasValue)
                query = query.Where(o => (o.PrepareAt ?? o.CreatedAt) >= fromDate.Value);
            if (toDate.HasValue)
                query = query.Where(o => (o.PrepareAt ?? o.CreatedAt) <= toDate.Value);
        }

        return await query.AverageAsync(o => (decimal?)o.Total, cancellationToken) ?? 0;
    }

    public async Task<List<Order>> GetTopSellingProductsAsync(int? branchId = null, DateTime? fromDate = null, DateTime? toDate = null, int limit = 10, CancellationToken cancellationToken = default)
    {
        var query = _context.OrderDetails
            .AsNoTracking()
            .Include(od => od.Product)
            .Include(od => od.Order)
            .Where(od => od.Order.Status != OrderStatus.Cancelled)
            .AsQueryable();

        if (branchId.HasValue)
            query = query.Where(od => od.Order.BranchId == branchId);

        if (fromDate.HasValue && toDate.HasValue)
            query = ApplyOrderDetailSalesDateRange(query, fromDate.Value, toDate.Value);
        else
        {
            if (fromDate.HasValue)
                query = query.Where(od => (od.Order.PrepareAt ?? od.Order.CreatedAt) >= fromDate.Value);
            if (toDate.HasValue)
                query = query.Where(od => (od.Order.PrepareAt ?? od.Order.CreatedAt) <= toDate.Value);
        }

        return await query
            .GroupBy(od => od.ProductId)
            .Select(g => new { ProductId = g.Key, TotalQuantity = g.Sum(od => od.Quantity) })
            .OrderByDescending(x => x.TotalQuantity)
            .Take(limit)
            .Join(_context.Products, x => x.ProductId, p => p.Id, (x, p) => new Order { Id = x.ProductId })
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> CanAssignDeliveryManAsync(int orderId, int deliveryManId, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders.FindAsync([orderId], cancellationToken);
        if (order == null ||
            (order.Status != OrderStatus.Ready &&
             order.Status != OrderStatus.OnTheWay &&
             order.Status != OrderStatus.Delivered))
            return false;

        return true;
    }

    public async Task<bool> CanCancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders.FindAsync([orderId], cancellationToken);
        return order != null && order.Status != OrderStatus.Cancelled;
    }

    public async Task<bool> CanChangeStatusAsync(int orderId, OrderStatus newStatus, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders.FindAsync([orderId], cancellationToken);
        // Las transiciones dependen del rol y se validan en
        // OrderBusinessRulesService antes de llegar al repositorio. Esta capa no
        // tiene contexto del usuario y no debe volver a imponer un flujo global.
        return order != null && Enum.IsDefined(newStatus);
    }

    public async Task<bool> HasActiveOrdersAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .AnyAsync(o => o.CustomerId == customerId &&
                         o.Status != OrderStatus.Delivered &&
                         o.Status != OrderStatus.Cancelled, cancellationToken);
    }

    public async Task<bool> HasOrdersInProgressAsync(int deliveryManId, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .AnyAsync(o => o.DeliveryManId == deliveryManId &&
                         (o.Status == OrderStatus.OnTheWay || o.Status == OrderStatus.Ready), cancellationToken);
    }

    public async Task<Order> ChangeStatusAsync(int orderId, OrderStatus newStatus, string? reason = null, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders.FindAsync([orderId], cancellationToken);
        if (order == null)
            throw new BusinessException("Pedido no encontrado");

        if (!await CanChangeStatusAsync(orderId, newStatus, cancellationToken))
            throw new BusinessException($"No se puede cambiar el estado de {order.Status} a {newStatus}.");

        var wasCancelled = order.Status == OrderStatus.Cancelled;

        order.Status = newStatus;
        order.AddStatusTime(newStatus, _clock.UtcNow);

        if (newStatus == OrderStatus.Cancelled && !string.IsNullOrEmpty(reason))
            order.CancelledReason = reason;
        else if (wasCancelled && newStatus != OrderStatus.Cancelled)
            order.CancelledReason = null;

        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(orderId, cancellationToken) ?? order;
    }

    public async Task<Order> AssignDeliveryManAsync(int orderId, int deliveryManId, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders.FindAsync([orderId], cancellationToken);
        if (order == null)
            throw new NotFoundException("Pedido no encontrado");

        if (order.Status != OrderStatus.Ready &&
            order.Status != OrderStatus.OnTheWay &&
            order.Status != OrderStatus.Delivered)
            throw new BusinessException($"El pedido debe estar en estado 'Ready', 'OnTheWay' o 'Delivered' para asignar/cambiar domiciliario. Estado actual: {order.Status}");

        order.DeliveryManId = deliveryManId;
        order.TouchDeliveryManAssignedAtUtc(_clock.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
        return order;
    }

    public async Task<Order> UnassignDeliveryManAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders.FindAsync([orderId], cancellationToken);
        if (order == null)
            throw new ArgumentException("Order not found");

        order.DeliveryManId = null;
        await _context.SaveChangesAsync(cancellationToken);
        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId, string reason, CancellationToken cancellationToken = default)
    {
        return await ChangeStatusAsync(orderId, OrderStatus.Cancelled, reason, cancellationToken);
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
        bool includeOnsiteActiveInAssignedHistory = false,
        string? totalDigitsPrefix = null,
        int? appId = null,
        bool appPaymentsUnsettledOnly = false,
        bool transferVerificationMode = false,
        CancellationToken cancellationToken = default)
    {
        // PostgreSQL timestamp with time zone requiere UTC
        if (fromDate.HasValue && fromDate.Value.Kind != DateTimeKind.Utc)
            fromDate = DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Utc);
        if (toDate.HasValue && toDate.Value.Kind != DateTimeKind.Utc)
            toDate = DateTime.SpecifyKind(toDate.Value, DateTimeKind.Utc);

        // Fase 1: consulta ligera (sin includes) — filtros + orden + ids paginados
        var query = _context.Orders.AsNoTracking().AsQueryable();

        query = query.ApplyOrderSearchTermFilter(_context, searchTerm);
        query = query.ApplyOrderTotalDigitsPrefix(totalDigitsPrefix);
        query = query.ApplyOrderAppPaymentFilters(appId, appPaymentsUnsettledOnly);

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

        if (!transferVerificationMode && fromDate.HasValue && toDate.HasValue)
        {
            var fromUtc = fromDate.Value.Kind == DateTimeKind.Utc ? fromDate.Value : DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Utc);
            var toUtc = toDate.Value.Kind == DateTimeKind.Utc ? toDate.Value : DateTime.SpecifyKind(toDate.Value, DateTimeKind.Utc);
            query = query.Where(o =>
                (o.CreatedAt >= fromUtc && o.CreatedAt <= toUtc)
                || (o.ReservedFor.HasValue && o.ReservedFor.Value >= fromUtc && o.ReservedFor.Value <= toUtc));
        }
        else if (!transferVerificationMode && fromDate.HasValue)
        {
            var fromUtc = fromDate.Value.Kind == DateTimeKind.Utc ? fromDate.Value : DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Utc);
            query = query.Where(o => o.CreatedAt >= fromUtc);
        }
        else if (!transferVerificationMode && toDate.HasValue)
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

        if (!transferVerificationMode && excludeFutureReservations)
        {
            var startOfTomorrowColombiaUtc = ColombiaTimeHelper.GetColombiaStartOfTomorrowUtcFromUtc(_clock.UtcNow);
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
        {
            if (transferVerificationMode && fromDate.HasValue && toDate.HasValue)
            {
                query = query.Where(o => o.BankPayments.Any(bp =>
                    bp.BankId == bankId.Value
                    && !bp.IsVerified
                    && bp.CreatedAt >= fromDate.Value
                    && bp.CreatedAt <= toDate.Value));
            }
            else
            {
                query = query.Where(o => o.BankPayments.Any(bp => bp.BankId == bankId.Value));
            }
        }

        if (neighborhoodId.HasValue)
            query = query.Where(o => o.Address != null && o.Address.NeighborhoodId == neighborhoodId.Value);

        if (transferVerificationMode && bankId.HasValue && fromDate.HasValue && toDate.HasValue)
        {
            query = query
                .OrderBy(o => o.BankPayments
                    .Where(bp => bp.BankId == bankId.Value
                        && !bp.IsVerified
                        && bp.CreatedAt >= fromDate.Value
                        && bp.CreatedAt <= toDate.Value)
                    .Select(bp => (decimal?)bp.Amount)
                    .Max() ?? 0m)
                .ThenBy(o => o.Id);
        }
        else
        {
            query = ApplySorting(query, sortBy, sortOrder);
        }

        var safePage = Math.Max(1, page);
        var safePageSize = Math.Max(1, pageSize);
        var total = await query.CountAsync(cancellationToken);
        var ids = await query
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
        {
            return new PagedResult<Order>
            {
                Items = [],
                TotalCount = total,
                Page = safePage,
                PageSize = safePageSize,
                TotalPages = (int)Math.Ceiling(total / (double)safePageSize),
            };
        }

        var orders = await OrdersWithListDetailIncludes()
            .Where(o => ids.Contains(o.Id))
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var byId = orders.ToDictionary(o => o.Id);
        var ordered = new List<Order>(ids.Count);
        foreach (var id in ids)
        {
            if (byId.TryGetValue(id, out var o))
                ordered.Add(o);
        }

        return new PagedResult<Order>
        {
            Items = ordered,
            TotalCount = total,
            Page = safePage,
            PageSize = safePageSize,
            TotalPages = (int)Math.Ceiling(total / (double)safePageSize),
        };
    }

    /// <inheritdoc />
    public async Task<PagedResult<Order>> SearchDeliveredOrdersByDeliveredAtRangeAsync(
        int? branchId,
        int? deliveryManId,
        OrderType type,
        DateTime fromUtc,
        DateTime toUtc,
        int page = 1,
        int pageSize = 500,
        CancellationToken cancellationToken = default)
    {
        if (type != OrderType.Delivery && type != OrderType.Onsite)
            throw new ArgumentOutOfRangeException(nameof(type), type, "Solo delivery u onsite.");

        if (fromUtc.Kind != DateTimeKind.Utc)
            fromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        if (toUtc.Kind != DateTimeKind.Utc)
            toUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);

        var typeDb = type == OrderType.Delivery ? "delivery" : "onsite";
        var safePageSize = Math.Max(1, pageSize);
        var offset = Math.Max(0, (Math.Max(1, page) - 1) * safePageSize);

        // Placeholders {0},{1},… para SqlQueryRaw (parametrizado por EF/Npgsql).
        // Excluir cadenas vacías y no-ISO antes del cast para evitar error 500 en datos legacy/corruptos.
        var conditions = new List<string>
        {
            "o.status = 'delivered'",
            "o.delivery_man_id IS NOT NULL",
            "o.type = {0}",
            "(o.status_times ->> 'delivered') IS NOT NULL",
            "trim(coalesce(o.status_times ->> 'delivered', '')) <> ''",
            // Llaves duplicadas: SqlQueryRaw usa string.Format; {{n}} llega como {n} en el regex de PostgreSQL.
            "(o.status_times ->> 'delivered') ~ '^[0-9]{{4}}-[0-9]{{2}}-[0-9]{{2}}[Tt ][0-9]{{2}}:[0-9]{{2}}'",
            "(o.status_times ->> 'delivered')::timestamptz >= {1}",
            "(o.status_times ->> 'delivered')::timestamptz <= {2}",
        };
        var sqlParams = new List<object> { typeDb, fromUtc, toUtc };
        if (branchId.HasValue)
        {
            conditions.Add($"o.branch_id = {{{sqlParams.Count}}}");
            sqlParams.Add(branchId.Value);
        }

        if (deliveryManId.HasValue)
        {
            conditions.Add($"o.delivery_man_id = {{{sqlParams.Count}}}");
            sqlParams.Add(deliveryManId.Value);
        }

        var whereSql = string.Join("\n  AND ", conditions);
        var sqlCount = $@"SELECT count(*)::bigint AS ""Total""
FROM ""order"" o
WHERE {whereSql}";

        var countRow = await _context.Database
            .SqlQueryRaw<DeliveredAtRangeCountRow>(sqlCount, sqlParams.ToArray())
            .SingleAsync(cancellationToken);

        var totalLong = countRow.Total;
        var total = (int)Math.Min(totalLong, int.MaxValue);

        var sqlParamsIds = new List<object>(sqlParams) { offset, safePageSize };
        var sqlIds = $@"SELECT o.id AS ""Id""
FROM ""order"" o
WHERE {whereSql}
ORDER BY (o.status_times ->> 'delivered')::timestamptz DESC
OFFSET {{{sqlParams.Count}}} LIMIT {{{sqlParams.Count + 1}}}";

        var idRows = await _context.Database
            .SqlQueryRaw<DeliveredAtRangeIdRow>(sqlIds, sqlParamsIds.ToArray())
            .ToListAsync(cancellationToken);
        var ids = idRows.Select(r => r.Id).ToList();

        if (ids.Count == 0)
        {
            return new PagedResult<Order>
            {
                Items = [],
                TotalCount = total,
                Page = page,
                PageSize = safePageSize,
                TotalPages = (int)Math.Ceiling(total / (double)safePageSize),
            };
        }

        var orders = await OrdersWithListDetailIncludes()
            .Where(o => ids.Contains(o.Id))
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var byId = orders.ToDictionary(o => o.Id);
        var ordered = new List<Order>(ids.Count);
        foreach (var id in ids)
        {
            if (byId.TryGetValue(id, out var o))
                ordered.Add(o);
        }

        return new PagedResult<Order>
        {
            Items = ordered,
            TotalCount = total,
            Page = page,
            PageSize = safePageSize,
            TotalPages = (int)Math.Ceiling(total / (double)safePageSize),
        };
    }

    /// <summary>Incluye el grafo usado en listados de pedido (búsqueda, cuadre domiciliarios, etc.).</summary>
    private IQueryable<Order> OrdersWithListDetailIncludes() =>
        _context.Orders
            .Include(o => o.Branch)
            .Include(o => o.TakenBy)
            .Include(o => o.Customer)
            .Include(o => o.Address)
                .ThenInclude(a => a!.Neighborhood)
            .Include(o => o.LoyaltyCycleStep)
            .Include(o => o.DeliveryMan)
            .Include(o => o.DeliveryRoute)
            .Include(o => o.DeliveryRouteStop)
            .Include(o => o.BankPayments)
                .ThenInclude(bp => bp.Bank)
                    .ThenInclude(b => b.Branch)
            .Include(o => o.AppPayments)
                .ThenInclude(ap => ap.App)
                    .ThenInclude(a => a.Bank)
                        .ThenInclude(b => b.Branch)
            .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                    .ThenInclude(p => p.Category);

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
            "prepareat" => isDescending
                ? query.OrderByDescending(o => o.PrepareAt.HasValue).ThenByDescending(o => o.PrepareAt)
                : query.OrderBy(o => o.PrepareAt == null).ThenBy(o => o.PrepareAt),
            "reservedfor" => isDescending ? query.OrderByDescending(o => o.ReservedFor) : query.OrderBy(o => o.ReservedFor),
            _ => isDescending ? query.OrderByDescending(o => o.CreatedAt) : query.OrderBy(o => o.CreatedAt)
        };
    }

    public async Task<IEnumerable<Order>> GetReservationsDueForPreparation(
        DateTime fromTime,
        DateTime toTime,
        OrderStatus status,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        return await _context.Orders
            .AsNoTracking()
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
            .ToListAsync(cancellationToken);
    }

    public async Task<PrincipalKpiSnapshot> GetPrincipalKpiSnapshotAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var q = ApplyOrderSalesDateRange(_context.Orders.AsNoTracking(), fromUtc, toUtc);
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

        var q = _context.Orders.AsNoTracking().Where(o => statuses.Contains(o.Status));
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
            .AsNoTracking()
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
        var q = ApplyOrderSalesDateRange(
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
        int? dayOfWeek = null,
        CancellationToken cancellationToken = default)
    {
        dayOfWeek = NormalizeDashboardDayOfWeek(dayOfWeek);
        if (dayOfWeek.HasValue)
        {
            var rows = await DashboardNonCancelledOrdersInRange(branchId, fromUtc, toUtc)
                .Select(o => new { o.BranchId, o.Type, o.Total, o.CreatedAt, o.PrepareAt })
                .ToListAsync(cancellationToken);

            return rows
                .Where(o => IsDashboardDayOfWeek(o.CreatedAt, o.PrepareAt, dayOfWeek.Value))
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
                .ToList();
        }

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
        int? dayOfWeek = null,
        CancellationToken cancellationToken = default)
    {
        dayOfWeek = NormalizeDashboardDayOfWeek(dayOfWeek);
        var rows = await DashboardNonCancelledOrdersInRange(branchId, fromUtc, toUtc)
            .Select(o => new { o.BranchId, o.CreatedAt, o.PrepareAt, o.Total })
            .ToListAsync(cancellationToken);

        return rows
            .Where(o => !dayOfWeek.HasValue || IsDashboardDayOfWeek(o.CreatedAt, o.PrepareAt, dayOfWeek.Value))
            .GroupBy(o => new
            {
                o.BranchId,
                Day = ColombiaTimeHelper.OrderSalesEffectiveColombiaCalendarDate(o.CreatedAt, o.PrepareAt),
            })
            .Select(g => new SalesDayPoint(g.Key.BranchId, g.Key.Day, g.Sum(x => x.Total)))
            .ToList();
    }

    public async Task<List<OrdersDayPoint>> GetDashboardOrdersByDayAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        int? dayOfWeek = null,
        CancellationToken cancellationToken = default)
    {
        dayOfWeek = NormalizeDashboardDayOfWeek(dayOfWeek);
        var rows = await DashboardNonCancelledOrdersInRange(branchId, fromUtc, toUtc)
            .Select(o => new { o.CreatedAt, o.PrepareAt })
            .ToListAsync(cancellationToken);

        return rows
            .Where(o => !dayOfWeek.HasValue || IsDashboardDayOfWeek(o.CreatedAt, o.PrepareAt, dayOfWeek.Value))
            .GroupBy(o => ColombiaTimeHelper.OrderSalesEffectiveColombiaCalendarDate(o.CreatedAt, o.PrepareAt))
            .Select(g => new OrdersDayPoint(g.Key, g.Count()))
            .ToList();
    }

    public async Task<List<SalesMonthPoint>> GetDashboardSalesByMonthAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        int? dayOfWeek = null,
        CancellationToken cancellationToken = default)
    {
        dayOfWeek = NormalizeDashboardDayOfWeek(dayOfWeek);
        var rows = await DashboardNonCancelledOrdersInRange(branchId, fromUtc, toUtc)
            .Select(o => new { o.BranchId, o.CreatedAt, o.PrepareAt, o.Total })
            .ToListAsync(cancellationToken);

        return rows
            .Where(o => !dayOfWeek.HasValue || IsDashboardDayOfWeek(o.CreatedAt, o.PrepareAt, dayOfWeek.Value))
            .GroupBy(o => new
            {
                o.BranchId,
                Ym = ColombiaTimeHelper.OrderSalesEffectiveColombiaYearMonth(o.CreatedAt, o.PrepareAt),
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
        int? dayOfWeek = null,
        CancellationToken cancellationToken = default)
    {
        dayOfWeek = NormalizeDashboardDayOfWeek(dayOfWeek);
        var rows = await DashboardNonCancelledOrdersInRange(branchId, fromUtc, toUtc)
            .Select(o => new { o.CreatedAt, o.PrepareAt })
            .ToListAsync(cancellationToken);

        return rows
            .Where(o => !dayOfWeek.HasValue || IsDashboardDayOfWeek(o.CreatedAt, o.PrepareAt, dayOfWeek.Value))
            .GroupBy(o => ColombiaTimeHelper.OrderSalesEffectiveColombiaYearMonth(o.CreatedAt, o.PrepareAt))
            .Select(g => new OrdersMonthPoint(g.Key.Year, g.Key.Month, g.Count()))
            .ToList();
    }

    public async Task<List<SalesYearPoint>> GetDashboardSalesByYearAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        int? dayOfWeek = null,
        CancellationToken cancellationToken = default)
    {
        dayOfWeek = NormalizeDashboardDayOfWeek(dayOfWeek);
        var rows = await DashboardNonCancelledOrdersInRange(branchId, fromUtc, toUtc)
            .Select(o => new { o.BranchId, o.CreatedAt, o.PrepareAt, o.Total })
            .ToListAsync(cancellationToken);

        return rows
            .Where(o => !dayOfWeek.HasValue || IsDashboardDayOfWeek(o.CreatedAt, o.PrepareAt, dayOfWeek.Value))
            .GroupBy(o => new
            {
                o.BranchId,
                Year = ColombiaTimeHelper.OrderSalesEffectiveColombiaYear(o.CreatedAt, o.PrepareAt),
            })
            .Select(g => new SalesYearPoint(g.Key.BranchId, g.Key.Year, g.Sum(x => x.Total)))
            .ToList();
    }

    public async Task<List<OrdersYearPoint>> GetDashboardOrdersByYearAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        int? dayOfWeek = null,
        CancellationToken cancellationToken = default)
    {
        dayOfWeek = NormalizeDashboardDayOfWeek(dayOfWeek);
        var rows = await DashboardNonCancelledOrdersInRange(branchId, fromUtc, toUtc)
            .Select(o => new { o.CreatedAt, o.PrepareAt })
            .ToListAsync(cancellationToken);

        return rows
            .Where(o => !dayOfWeek.HasValue || IsDashboardDayOfWeek(o.CreatedAt, o.PrepareAt, dayOfWeek.Value))
            .GroupBy(o => ColombiaTimeHelper.OrderSalesEffectiveColombiaYear(o.CreatedAt, o.PrepareAt))
            .Select(g => new OrdersYearPoint(g.Key, g.Count()))
            .ToList();
    }

    public async Task<List<SalesHourPoint>> GetDashboardSalesByHourAsync(
        int? branchId,
        DateTime dayStartUtc,
        DateTime dayEndUtc,
        int? dayOfWeek = null,
        CancellationToken cancellationToken = default)
    {
        dayOfWeek = NormalizeDashboardDayOfWeek(dayOfWeek);
        var rows = await DashboardNonCancelledOrdersInRange(branchId, dayStartUtc, dayEndUtc)
            .Select(o => new { o.BranchId, o.CreatedAt, o.PrepareAt, o.Total })
            .ToListAsync(cancellationToken);

        return rows
            .Where(o => !dayOfWeek.HasValue || IsDashboardDayOfWeek(o.CreatedAt, o.PrepareAt, dayOfWeek.Value))
            .GroupBy(o => new
            {
                o.BranchId,
                Hour = ColombiaTimeHelper.OrderSalesEffectiveColombiaHour(o.CreatedAt, o.PrepareAt),
            })
            .Select(g => new SalesHourPoint(g.Key.BranchId, g.Key.Hour, g.Sum(x => x.Total)))
            .ToList();
    }

    public async Task<List<OrdersHourPoint>> GetDashboardOrdersByHourAsync(
        int? branchId,
        DateTime dayStartUtc,
        DateTime dayEndUtc,
        int? dayOfWeek = null,
        CancellationToken cancellationToken = default)
    {
        dayOfWeek = NormalizeDashboardDayOfWeek(dayOfWeek);
        var rows = await DashboardNonCancelledOrdersInRange(branchId, dayStartUtc, dayEndUtc)
            .Select(o => new { o.CreatedAt, o.PrepareAt })
            .ToListAsync(cancellationToken);

        return rows
            .Where(o => !dayOfWeek.HasValue || IsDashboardDayOfWeek(o.CreatedAt, o.PrepareAt, dayOfWeek.Value))
            .GroupBy(o => ColombiaTimeHelper.OrderSalesEffectiveColombiaHour(o.CreatedAt, o.PrepareAt))
            .Select(g => new OrdersHourPoint(g.Key, g.Count()))
            .ToList();
    }

    public async Task<List<SalesHourlyAnalyticsPoint>> GetDashboardSalesHourlyAnalyticsAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        int? dayOfWeek = null,
        CancellationToken cancellationToken = default)
    {
        dayOfWeek = NormalizeDashboardDayOfWeek(dayOfWeek);

        var dailyHourBuckets = await GetDashboardSalesDailyHourBucketsAsync(
            branchId,
            fromUtc,
            toUtc,
            cancellationToken);

        if (dayOfWeek.HasValue)
            dailyHourBuckets = dailyHourBuckets.Where(b => b.DayOfWeek == dayOfWeek.Value).ToList();

        return dailyHourBuckets
            .GroupBy(b => b.Hour)
            .Select(g =>
            {
                var orderedTotals = g.Select(x => (decimal)x.TotalSalesCop).OrderBy(x => x).ToList();
                var totalSales = g.Sum(x => x.TotalSalesCop);
                var orderCount = g.Sum(x => x.OrderCount);

                return new SalesHourlyAnalyticsPoint(
                    g.Key,
                    orderCount,
                    totalSales,
                    g.Average(x => (decimal)x.TotalSalesCop),
                    PercentileCont(orderedTotals, 0.5m),
                    orderCount == 0 ? 0 : (decimal)totalSales / orderCount);
            })
            .OrderBy(p => p.Hour)
            .ToList();
    }

    public async Task<List<SalesDailyHourBucket>> GetDashboardSalesDailyHourBucketsAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var rows = await DashboardNonCancelledOrdersInRange(branchId, fromUtc, toUtc)
            .Select(o => new { o.CreatedAt, o.PrepareAt, o.Total })
            .ToListAsync(cancellationToken);

        return rows
            .Select(o =>
            {
                var day = ColombiaTimeHelper.OrderSalesEffectiveColombiaCalendarDate(o.CreatedAt, o.PrepareAt);
                return new
                {
                    Day = day,
                    DayOfWeek = IsoDayOfWeek(day),
                    Hour = ColombiaTimeHelper.OrderSalesEffectiveColombiaHour(o.CreatedAt, o.PrepareAt),
                    o.Total,
                };
            })
            .GroupBy(o => new { o.Day, o.DayOfWeek, o.Hour })
            .Select(g => new SalesDailyHourBucket(
                g.Key.Day,
                g.Key.DayOfWeek,
                g.Key.Hour,
                g.Count(),
                g.Sum(x => (long)x.Total)))
            .OrderBy(b => b.Day)
            .ThenBy(b => b.Hour)
            .ToList();
    }

    public async Task<List<SalesProductAggregateRow>> GetSalesProductAggregatesForDashboardAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        int? dayOfWeek = null,
        CancellationToken cancellationToken = default)
    {
        dayOfWeek = NormalizeDashboardDayOfWeek(dayOfWeek);
        var q = ApplyOrderDetailSalesDateRange(
            _context.OrderDetails
                .AsNoTracking()
                .Where(od => od.Order.Status != OrderStatus.Cancelled),
            fromUtc,
            toUtc);

        if (branchId.HasValue)
            q = q.Where(od => od.Order.BranchId == branchId.Value);

        if (dayOfWeek.HasValue)
        {
            var rows = await q
                .Select(od => new
                {
                    od.ProductId,
                    ProductName = od.Product.Name ?? string.Empty,
                    od.Quantity,
                    RevenueCop = (long)(od.Subtotal ?? (od.Quantity * od.UnitPrice - od.Discount)),
                    od.Order.CreatedAt,
                    od.Order.PrepareAt,
                })
                .ToListAsync(cancellationToken);

            return rows
                .Where(od => IsDashboardDayOfWeek(od.CreatedAt, od.PrepareAt, dayOfWeek.Value))
                .GroupBy(od => new { od.ProductId, od.ProductName })
                .Select(g => new SalesProductAggregateRow
                {
                    ProductId = g.Key.ProductId,
                    ProductName = g.Key.ProductName,
                    QuantitySold = g.Sum(od => od.Quantity),
                    RevenueCop = g.Sum(od => od.RevenueCop),
                })
                .ToList();
        }

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
        int? dayOfWeek = null,
        CancellationToken cancellationToken = default)
    {
        dayOfWeek = NormalizeDashboardDayOfWeek(dayOfWeek);
        var q = ApplyOrderDetailSalesDateRange(
            _context.OrderDetails
                .AsNoTracking()
                .Where(od => od.Order.Status != OrderStatus.Cancelled),
            fromUtc,
            toUtc);

        if (branchId.HasValue)
            q = q.Where(od => od.Order.BranchId == branchId.Value);

        if (dayOfWeek.HasValue)
        {
            var rows = await q
                .Select(od => new
                {
                    od.ProductId,
                    ProductName = od.Product.Name ?? string.Empty,
                    od.Product.CategoryId,
                    CategoryName = od.Product.Category.Name ?? string.Empty,
                    od.Quantity,
                    RevenueCop = (long)(od.Subtotal ?? (od.Quantity * od.UnitPrice - od.Discount)),
                    od.Order.CreatedAt,
                    od.Order.PrepareAt,
                })
                .ToListAsync(cancellationToken);

            return rows
                .Where(od => IsDashboardDayOfWeek(od.CreatedAt, od.PrepareAt, dayOfWeek.Value))
                .GroupBy(od => new { od.ProductId, od.ProductName, od.CategoryId, od.CategoryName })
                .Select(g => new SalesProductCategoryAggregateRow
                {
                    ProductId = g.Key.ProductId,
                    ProductName = g.Key.ProductName,
                    CategoryId = g.Key.CategoryId,
                    CategoryName = g.Key.CategoryName,
                    QuantitySold = g.Sum(od => od.Quantity),
                    RevenueCop = g.Sum(od => od.RevenueCop),
                })
                .ToList();
        }

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
        int? dayOfWeek = null,
        CancellationToken cancellationToken = default)
    {
        dayOfWeek = NormalizeDashboardDayOfWeek(dayOfWeek);
        var q = ApplyOrderDetailSalesDateRange(
            _context.OrderDetails
                .AsNoTracking()
                .Where(od => od.Order.Status != OrderStatus.Cancelled),
            fromUtc,
            toUtc);

        if (branchId.HasValue)
            q = q.Where(od => od.Order.BranchId == branchId.Value);

        if (dayOfWeek.HasValue)
        {
            var rows = await q
                .Select(od => new
                {
                    od.Product.CategoryId,
                    CategoryName = od.Product.Category.Name ?? string.Empty,
                    od.Quantity,
                    RevenueCop = (long)(od.Subtotal ?? (od.Quantity * od.UnitPrice - od.Discount)),
                    od.Order.CreatedAt,
                    od.Order.PrepareAt,
                })
                .ToListAsync(cancellationToken);

            return rows
                .Where(od => IsDashboardDayOfWeek(od.CreatedAt, od.PrepareAt, dayOfWeek.Value))
                .GroupBy(od => new { od.CategoryId, od.CategoryName })
                .Select(g => new SalesCategoryAggregateRow
                {
                    CategoryId = g.Key.CategoryId,
                    CategoryName = g.Key.CategoryName,
                    QuantitySold = g.Sum(od => od.Quantity),
                    RevenueCop = g.Sum(od => od.RevenueCop),
                })
                .ToList();
        }

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
        int? dayOfWeek = null,
        CancellationToken cancellationToken = default)
    {
        dayOfWeek = NormalizeDashboardDayOfWeek(dayOfWeek);
        var q = ApplyOrderDetailSalesDateRange(
            _context.OrderDetails
                .AsNoTracking()
                .Where(od => od.Order.Status != OrderStatus.Cancelled && od.Product.WeightGrams != null),
            fromUtc,
            toUtc);

        if (branchId.HasValue)
            q = q.Where(od => od.Order.BranchId == branchId.Value);

        List<SalesCategoryWeightRow> rows;
        if (dayOfWeek.HasValue)
        {
            var rawRows = await q
                .Select(od => new
                {
                    od.Product.CategoryId,
                    CategoryName = od.Product.Category.Name ?? string.Empty,
                    od.Quantity,
                    WeightGrams = od.Product.WeightGrams!.Value,
                    od.Order.CreatedAt,
                    od.Order.PrepareAt,
                })
                .ToListAsync(cancellationToken);

            rows = rawRows
                .Where(od => IsDashboardDayOfWeek(od.CreatedAt, od.PrepareAt, dayOfWeek.Value))
                .GroupBy(od => new { od.CategoryId, od.CategoryName })
                .Select(g => new SalesCategoryWeightRow
                {
                    CategoryId = g.Key.CategoryId,
                    CategoryName = g.Key.CategoryName,
                    TotalWeightGrams = g.Sum(od => (long)od.Quantity * od.WeightGrams),
                })
                .ToList();
        }
        else
        {
            rows = await q
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
        }

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
        int? dayOfWeek = null,
        CancellationToken cancellationToken = default)
    {
        dayOfWeek = NormalizeDashboardDayOfWeek(dayOfWeek);
        var q = ApplyOrderDetailSalesDateRange(
            _context.OrderDetails
                .AsNoTracking()
                .Where(od => od.Order.Status != OrderStatus.Cancelled && od.Product.WeightGrams != null),
            fromUtc,
            toUtc);

        if (branchId.HasValue)
            q = q.Where(od => od.Order.BranchId == branchId.Value);

        List<SalesProductWeightRow> rows;
        if (dayOfWeek.HasValue)
        {
            var rawRows = await q
                .Select(od => new
                {
                    od.ProductId,
                    ProductName = od.Product.Name ?? string.Empty,
                    od.Quantity,
                    WeightGrams = od.Product.WeightGrams!.Value,
                    od.Order.CreatedAt,
                    od.Order.PrepareAt,
                })
                .ToListAsync(cancellationToken);

            rows = rawRows
                .Where(od => IsDashboardDayOfWeek(od.CreatedAt, od.PrepareAt, dayOfWeek.Value))
                .GroupBy(od => new { od.ProductId, od.ProductName })
                .Select(g => new SalesProductWeightRow
                {
                    ProductId = g.Key.ProductId,
                    ProductName = g.Key.ProductName,
                    TotalWeightGrams = g.Sum(od => (long)od.Quantity * od.WeightGrams),
                })
                .ToList();
        }
        else
        {
            rows = await q
                .GroupBy(od => new { od.ProductId, Name = od.Product.Name ?? string.Empty })
                .Select(g => new SalesProductWeightRow
                {
                    ProductId = g.Key.ProductId,
                    ProductName = g.Key.Name,
                    TotalWeightGrams = g.Sum(od => (long)od.Quantity * od.Product.WeightGrams!.Value),
                })
                .ToListAsync(cancellationToken);
        }

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
        int? dayOfWeek = null,
        CancellationToken cancellationToken = default)
    {
        dayOfWeek = NormalizeDashboardDayOfWeek(dayOfWeek);
        var q = ApplyOrderDetailSalesDateRange(
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
            CategoryWeightEvolutionGranularity.Day => await EvolutionByDayAsync(q, dayOfWeek, cancellationToken),
            CategoryWeightEvolutionGranularity.Month => await EvolutionByMonthAsync(q, dayOfWeek, cancellationToken),
            CategoryWeightEvolutionGranularity.Year => await EvolutionByYearAsync(q, dayOfWeek, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, null),
        };
    }

    public async Task<List<SalesCategoryWeightEvolutionSeries>> GetSalesCategoryWeightEvolutionAllCategoriesAsync(
        int? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CategoryWeightEvolutionGranularity granularity,
        int? dayOfWeek = null,
        CancellationToken cancellationToken = default)
    {
        dayOfWeek = NormalizeDashboardDayOfWeek(dayOfWeek);
        var q = ApplyOrderDetailSalesDateRange(
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
            CategoryWeightEvolutionGranularity.Day => await EvolutionAllCategoriesByDayAsync(q, dayOfWeek, cancellationToken),
            CategoryWeightEvolutionGranularity.Month => await EvolutionAllCategoriesByMonthAsync(q, dayOfWeek, cancellationToken),
            CategoryWeightEvolutionGranularity.Year => await EvolutionAllCategoriesByYearAsync(q, dayOfWeek, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, null),
        };
    }

    private static async Task<List<SalesCategoryWeightEvolutionPoint>> EvolutionByDayAsync(
        IQueryable<OrderDetail> q,
        int? dayOfWeek,
        CancellationToken cancellationToken)
    {
        var raw = await q
            .Select(od => new
            {
                od.Order.CreatedAt, od.Order.PrepareAt,
                W = (long)od.Quantity * od.Product.WeightGrams!.Value,
            })
            .ToListAsync(cancellationToken);

        return raw
            .Where(x => !dayOfWeek.HasValue || IsDashboardDayOfWeek(x.CreatedAt, x.PrepareAt, dayOfWeek.Value))
            .GroupBy(x => ColombiaTimeHelper.OrderSalesEffectiveColombiaCalendarDate(x.CreatedAt, x.PrepareAt))
            .Select(g => new SalesCategoryWeightEvolutionPoint(
                ColombiaTimeHelper.ColombiaCalendarDayStartUtc(g.Key),
                g.Sum(x => x.W)))
            .OrderBy(p => p.BucketStartUtc)
            .ToList();
    }

    private static async Task<List<SalesCategoryWeightEvolutionPoint>> EvolutionByMonthAsync(
        IQueryable<OrderDetail> q,
        int? dayOfWeek,
        CancellationToken cancellationToken)
    {
        var raw = await q
            .Select(od => new
            {
                od.Order.CreatedAt, od.Order.PrepareAt,
                W = (long)od.Quantity * od.Product.WeightGrams!.Value,
            })
            .ToListAsync(cancellationToken);

        return raw
            .Where(x => !dayOfWeek.HasValue || IsDashboardDayOfWeek(x.CreatedAt, x.PrepareAt, dayOfWeek.Value))
            .GroupBy(x => ColombiaTimeHelper.OrderSalesEffectiveColombiaYearMonth(x.CreatedAt, x.PrepareAt))
            .Select(g => new SalesCategoryWeightEvolutionPoint(
                ColombiaTimeHelper.ColombiaCalendarDayStartUtc(new DateTime(g.Key.Year, g.Key.Month, 1)),
                g.Sum(x => x.W)))
            .OrderBy(p => p.BucketStartUtc)
            .ToList();
    }

    private static async Task<List<SalesCategoryWeightEvolutionPoint>> EvolutionByYearAsync(
        IQueryable<OrderDetail> q,
        int? dayOfWeek,
        CancellationToken cancellationToken)
    {
        var raw = await q
            .Select(od => new
            {
                od.Order.CreatedAt, od.Order.PrepareAt,
                W = (long)od.Quantity * od.Product.WeightGrams!.Value,
            })
            .ToListAsync(cancellationToken);

        return raw
            .Where(x => !dayOfWeek.HasValue || IsDashboardDayOfWeek(x.CreatedAt, x.PrepareAt, dayOfWeek.Value))
            .GroupBy(x => ColombiaTimeHelper.OrderSalesEffectiveColombiaYear(x.CreatedAt, x.PrepareAt))
            .Select(g => new SalesCategoryWeightEvolutionPoint(
                ColombiaTimeHelper.ColombiaCalendarDayStartUtc(new DateTime(g.Key, 1, 1)),
                g.Sum(x => x.W)))
            .OrderBy(p => p.BucketStartUtc)
            .ToList();
    }

    private static async Task<List<SalesCategoryWeightEvolutionSeries>> EvolutionAllCategoriesByDayAsync(
        IQueryable<OrderDetail> q,
        int? dayOfWeek,
        CancellationToken cancellationToken)
    {
        var raw = await q
            .Select(od => new
            {
                od.Product.CategoryId,
                Name = od.Product.Category.Name ?? string.Empty,
                od.Order.CreatedAt, od.Order.PrepareAt,
                W = (long)od.Quantity * od.Product.WeightGrams!.Value,
            })
            .ToListAsync(cancellationToken);

        return raw
            .Where(x => !dayOfWeek.HasValue || IsDashboardDayOfWeek(x.CreatedAt, x.PrepareAt, dayOfWeek.Value))
            .GroupBy(x => (x.CategoryId, x.Name))
            .Select(g => new SalesCategoryWeightEvolutionSeries(
                g.Key.CategoryId,
                g.Key.Name,
                g.GroupBy(x => ColombiaTimeHelper.OrderSalesEffectiveColombiaCalendarDate(x.CreatedAt, x.PrepareAt))
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
        int? dayOfWeek,
        CancellationToken cancellationToken)
    {
        var raw = await q
            .Select(od => new
            {
                od.Product.CategoryId,
                Name = od.Product.Category.Name ?? string.Empty,
                od.Order.CreatedAt, od.Order.PrepareAt,
                W = (long)od.Quantity * od.Product.WeightGrams!.Value,
            })
            .ToListAsync(cancellationToken);

        return raw
            .Where(x => !dayOfWeek.HasValue || IsDashboardDayOfWeek(x.CreatedAt, x.PrepareAt, dayOfWeek.Value))
            .GroupBy(x => (x.CategoryId, x.Name))
            .Select(g => new SalesCategoryWeightEvolutionSeries(
                g.Key.CategoryId,
                g.Key.Name,
                g.GroupBy(x => ColombiaTimeHelper.OrderSalesEffectiveColombiaYearMonth(x.CreatedAt, x.PrepareAt))
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
        int? dayOfWeek,
        CancellationToken cancellationToken)
    {
        var raw = await q
            .Select(od => new
            {
                od.Product.CategoryId,
                Name = od.Product.Category.Name ?? string.Empty,
                od.Order.CreatedAt, od.Order.PrepareAt,
                W = (long)od.Quantity * od.Product.WeightGrams!.Value,
            })
            .ToListAsync(cancellationToken);

        return raw
            .Where(x => !dayOfWeek.HasValue || IsDashboardDayOfWeek(x.CreatedAt, x.PrepareAt, dayOfWeek.Value))
            .GroupBy(x => (x.CategoryId, x.Name))
            .Select(g => new SalesCategoryWeightEvolutionSeries(
                g.Key.CategoryId,
                g.Key.Name,
                g.GroupBy(x => ColombiaTimeHelper.OrderSalesEffectiveColombiaYear(x.CreatedAt, x.PrepareAt))
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
    /// Incluye pedidos creados en el rango UTC o con <see cref="Order.ReservedFor"/> en el mismo rango (día operativo histórico).
    /// Mantener para listados/auditoría que conservan semántica legacy.
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

    private static DateTime GetSalesEffectiveDate(DateTime createdAtUtc, DateTime? prepareAtUtc)
        => prepareAtUtc ?? createdAtUtc;

    private static int? NormalizeDashboardDayOfWeek(int? dayOfWeek)
    {
        if (!dayOfWeek.HasValue || dayOfWeek.Value < 1 || dayOfWeek.Value > 7)
            return null;
        return dayOfWeek.Value;
    }

    private static bool IsDashboardDayOfWeek(DateTime createdAtUtc, DateTime? prepareAtUtc, int dayOfWeek)
    {
        var day = ColombiaTimeHelper.OrderSalesEffectiveColombiaCalendarDate(createdAtUtc, prepareAtUtc);
        return IsoDayOfWeek(day) == dayOfWeek;
    }

    private static int IsoDayOfWeek(DateTime date)
        => date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;

    private static decimal PercentileCont(IReadOnlyList<decimal> sortedValues, decimal percentile)
    {
        if (sortedValues.Count == 0)
            return 0;
        if (sortedValues.Count == 1)
            return sortedValues[0];

        var rank = (sortedValues.Count - 1) * percentile;
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper)
            return sortedValues[lower];

        var fraction = rank - lower;
        return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * fraction;
    }

    private static IQueryable<Order> ApplyOrderSalesDateRange(
        IQueryable<Order> orders,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var from = fromUtc.Kind == DateTimeKind.Utc ? fromUtc : DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        var to = toUtc.Kind == DateTimeKind.Utc ? toUtc : DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);
        return orders.Where(o => (o.PrepareAt ?? o.CreatedAt) >= from && (o.PrepareAt ?? o.CreatedAt) <= to);
    }

    private static IQueryable<OrderDetail> ApplyOrderDetailSalesDateRange(
        IQueryable<OrderDetail> query,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var from = fromUtc.Kind == DateTimeKind.Utc ? fromUtc : DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        var to = toUtc.Kind == DateTimeKind.Utc ? toUtc : DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);
        return query.Where(od => (od.Order.PrepareAt ?? od.Order.CreatedAt) >= from && (od.Order.PrepareAt ?? od.Order.CreatedAt) <= to);
    }
}

