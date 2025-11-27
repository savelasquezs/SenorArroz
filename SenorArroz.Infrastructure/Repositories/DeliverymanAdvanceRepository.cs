using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Infrastructure.Repositories;

public class DeliverymanAdvanceRepository : IDeliverymanAdvanceRepository
{
    private readonly ApplicationDbContext _context;

    public DeliverymanAdvanceRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<DeliverymanAdvance>> GetPagedAsync(
        int? deliverymanId = null,
        int? branchId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "createdAt",
        string sortOrder = "desc")
    {
        var query = _context.DeliverymanAdvances
            .Include(da => da.Deliveryman)
            .Include(da => da.Creator)
            .Include(da => da.Branch)
            .AsQueryable();

        // Filtros
        if (deliverymanId.HasValue)
        {
            query = query.Where(da => da.DeliverymanId == deliverymanId.Value);
        }

        if (branchId.HasValue)
        {
            query = query.Where(da => da.BranchId == branchId.Value);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(da => da.CreatedAt >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(da => da.CreatedAt <= toDate.Value);
        }

        // Total count
        var totalCount = await query.CountAsync();

        // Ordenamiento
        query = sortBy.ToLower() switch
        {
            "amount" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(da => da.Amount)
                : query.OrderBy(da => da.Amount),
            "deliveryman" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(da => da.Deliveryman.Name)
                : query.OrderBy(da => da.Deliveryman.Name),
            "createdat" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(da => da.CreatedAt)
                : query.OrderBy(da => da.CreatedAt),
            _ => query.OrderByDescending(da => da.CreatedAt)
        };

        // Paginación
        var advances = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<DeliverymanAdvance>
        {
            Items = advances,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<DeliverymanAdvance?> GetByIdAsync(int id)
    {
        return await _context.DeliverymanAdvances
            .Include(da => da.Deliveryman)
            .Include(da => da.Creator)
            .Include(da => da.Branch)
            .FirstOrDefaultAsync(da => da.Id == id);
    }

    public async Task<IEnumerable<DeliverymanAdvance>> GetByDeliverymanIdAsync(
        int deliverymanId,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        var query = _context.DeliverymanAdvances
            .Include(da => da.Creator)
            .Include(da => da.Branch)
            .Where(da => da.DeliverymanId == deliverymanId);

        if (fromDate.HasValue)
        {
            query = query.Where(da => da.CreatedAt >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(da => da.CreatedAt <= toDate.Value);
        }

        return await query
            .OrderByDescending(da => da.CreatedAt)
            .ToListAsync();
    }

    public async Task<DeliverymanAdvance> CreateAsync(DeliverymanAdvance advance)
    {
        _context.DeliverymanAdvances.Add(advance);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(advance.Id) ?? advance;
    }

    public async Task<DeliverymanAdvance> UpdateAsync(DeliverymanAdvance advance)
    {
        _context.DeliverymanAdvances.Update(advance);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(advance.Id) ?? advance;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var advance = await _context.DeliverymanAdvances.FindAsync(id);
        if (advance == null)
            return false;

        _context.DeliverymanAdvances.Remove(advance);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(int id)
    {
        return await _context.DeliverymanAdvances.AnyAsync(da => da.Id == id);
    }

    public async Task<decimal> GetTotalAdvancesForDateAsync(int deliverymanId, DateTime date)
    {
        var total = await _context.DeliverymanAdvances
            .Where(da =>
                da.DeliverymanId == deliverymanId &&
                da.CreatedAt.Date == date.Date)
            .SumAsync(da => (decimal?)da.Amount);

        return total ?? 0;
    }

    public async Task<decimal> GetTotalAdvancesByDeliverymanAsync(
        int deliverymanId,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        var query = _context.DeliverymanAdvances
            .Where(da => da.DeliverymanId == deliverymanId);

        if (fromDate.HasValue)
        {
            query = query.Where(da => da.CreatedAt >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(da => da.CreatedAt <= toDate.Value);
        }

        var total = await query.SumAsync(da => (decimal?)da.Amount);
        return total ?? 0;
    }

    public async Task<int> GetCountByDeliverymanAsync(
        int deliverymanId,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        var query = _context.DeliverymanAdvances
            .Where(da => da.DeliverymanId == deliverymanId);

        if (fromDate.HasValue)
        {
            query = query.Where(da => da.CreatedAt >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(da => da.CreatedAt <= toDate.Value);
        }

        return await query.CountAsync();
    }
}

