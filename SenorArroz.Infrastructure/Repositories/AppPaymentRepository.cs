// SenorArroz.Infrastructure/Repositories/AppPaymentRepository.cs
using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Infrastructure.Repositories;

public class AppPaymentRepository : IAppPaymentRepository
{
    private readonly ApplicationDbContext _context;

    public AppPaymentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<AppPayment>> GetPagedAsync(
        int? orderId = null,
        int? appId = null,
        bool? settled = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "createdAt",
        string sortOrder = "desc")
    {
        var query = _context.AppPayments
            .Include(ap => ap.Order)
            .Include(ap => ap.App)
            .ThenInclude(a => a.Bank)
            .ThenInclude(b => b.Branch)
            .AsQueryable();

        // Order filter
        if (orderId.HasValue)
        {
            query = query.Where(ap => ap.OrderId == orderId.Value);
        }

        // App filter
        if (appId.HasValue)
        {
            query = query.Where(ap => ap.AppId == appId.Value);
        }

        // Settlement filter
        if (settled.HasValue)
        {
            query = query.Where(ap => ap.IsSetted == settled.Value);
        }

        // Date range filter
        if (fromDate.HasValue)
        {
            query = query.Where(ap => ap.CreatedAt >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(ap => ap.CreatedAt <= toDate.Value);
        }

        // Total count
        var totalCount = await query.CountAsync();

        // Sorting
        query = sortBy.ToLower() switch
        {
            "amount" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(ap => ap.Amount) : query.OrderBy(ap => ap.Amount),
            "createdat" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(ap => ap.CreatedAt) : query.OrderBy(ap => ap.CreatedAt),
            "app" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(ap => ap.App.Name) : query.OrderBy(ap => ap.App.Name),
            "order" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(ap => ap.OrderId) : query.OrderBy(ap => ap.OrderId),
            "settled" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(ap => ap.IsSetted) : query.OrderBy(ap => ap.IsSetted),
            _ => query.OrderByDescending(ap => ap.CreatedAt)
        };

        // Pagination
        var appPayments = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<AppPayment>
        {
            Items = appPayments,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<IEnumerable<AppPayment>> GetByOrderIdAsync(int orderId)
    {
        return await _context.AppPayments
            .Include(ap => ap.App)
            .ThenInclude(a => a.Bank)
            .ThenInclude(b => b.Branch)
            .Where(ap => ap.OrderId == orderId)
            .OrderBy(ap => ap.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<AppPayment>> GetByAppIdAsync(int appId)
    {
        return await _context.AppPayments
            .Include(ap => ap.Order)
            .Where(ap => ap.AppId == appId)
            .OrderByDescending(ap => ap.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<AppPayment>> GetUnsettledAsync()
    {
        return await _context.AppPayments
            .Include(ap => ap.App)
            .ThenInclude(a => a.Bank)
            .ThenInclude(b => b.Branch)
            .Include(ap => ap.Order)
            .Where(ap => !ap.IsSetted)
            .OrderBy(ap => ap.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<AppPayment>> GetUnsettledByAppIdAsync(int appId)
    {
        return await _context.AppPayments
            .Include(ap => ap.App)
            .ThenInclude(a => a.Bank)
            .ThenInclude(b => b.Branch)
            .Include(ap => ap.Order)
            .Where(ap => ap.AppId == appId && !ap.IsSetted)
            .OrderBy(ap => ap.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<AppPayment>> GetUnsettledByDateRangeAsync(DateTime fromDate, DateTime toDate)
    {
        return await _context.AppPayments
            .Include(ap => ap.App)
            .ThenInclude(a => a.Bank)
            .ThenInclude(b => b.Branch)
            .Include(ap => ap.Order)
            .Where(ap => !ap.IsSetted && ap.CreatedAt >= fromDate && ap.CreatedAt <= toDate)
            .OrderBy(ap => ap.CreatedAt)
            .ToListAsync();
    }

    public async Task<AppPayment?> GetByIdAsync(int id)
    {
        return await _context.AppPayments
            .Include(ap => ap.Order)
            .Include(ap => ap.App)
            .ThenInclude(a => a.Bank)
            .ThenInclude(b => b.Branch)
            .FirstOrDefaultAsync(ap => ap.Id == id);
    }

    public async Task<AppPayment> CreateAsync(AppPayment appPayment)
    {
        _context.AppPayments.Add(appPayment);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(appPayment.Id) ?? appPayment;
    }

    public async Task<AppPayment> UpdateAsync(AppPayment appPayment)
    {
        _context.AppPayments.Update(appPayment);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(appPayment.Id) ?? appPayment;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var appPayment = await _context.AppPayments.FindAsync(id);
        if (appPayment == null)
            return false;

        _context.AppPayments.Remove(appPayment);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(int id)
    {
        return await _context.AppPayments.AnyAsync(ap => ap.Id == id);
    }

    // Settlement methods
    public async Task<bool> SettlePaymentsAsync(IEnumerable<int> paymentIds)
    {
        var payments = await _context.AppPayments
            .Where(ap => paymentIds.Contains(ap.Id))
            .ToListAsync();

        if (!payments.Any())
            return false;

        foreach (var payment in payments)
        {
            payment.IsSetted = true;
        }

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UnsettlePaymentsAsync(IEnumerable<int> paymentIds)
    {
        var payments = await _context.AppPayments
            .Where(ap => paymentIds.Contains(ap.Id))
            .ToListAsync();

        if (!payments.Any())
            return false;

        foreach (var payment in payments)
        {
            payment.IsSetted = false;
        }

        await _context.SaveChangesAsync();
        return true;
    }

    // Statistics
    public async Task<decimal> GetTotalAmountByAppAsync(int appId, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var query = _context.AppPayments.Where(ap => ap.AppId == appId);

        if (fromDate.HasValue)
            query = query.Where(ap => ap.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(ap => ap.CreatedAt <= toDate.Value);

        return await query.SumAsync(ap => ap.Amount);
    }

    public async Task<decimal> GetTotalAmountByOrderAsync(int orderId)
    {
        return await _context.AppPayments
            .Where(ap => ap.OrderId == orderId)
            .SumAsync(ap => ap.Amount);
    }

    public async Task<decimal> GetUnsettledAmountByAppAsync(int appId)
    {
        return await _context.AppPayments
            .Where(ap => ap.AppId == appId && !ap.IsSetted)
            .SumAsync(ap => ap.Amount);
    }

    public async Task<int> GetTotalCountByAppAsync(int appId, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var query = _context.AppPayments.Where(ap => ap.AppId == appId);

        if (fromDate.HasValue)
            query = query.Where(ap => ap.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(ap => ap.CreatedAt <= toDate.Value);

        return await query.CountAsync();
    }

    public async Task<int> GetUnsettledCountByAppAsync(int appId)
    {
        return await _context.AppPayments
            .CountAsync(ap => ap.AppId == appId && !ap.IsSetted);
    }
}
