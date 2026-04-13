using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Common;
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
        string sortOrder = "desc",
        CancellationToken cancellationToken = default)
    {
        var query = _context.DeliverymanAdvances
            .AsNoTracking()
            .Include(da => da.Deliveryman)
            .Include(da => da.Creator)
            .Include(da => da.Branch)
            .Include(da => da.Bank)
            .AsQueryable();

        if (deliverymanId.HasValue)
            query = query.Where(da => da.DeliverymanId == deliverymanId.Value);

        if (branchId.HasValue)
            query = query.Where(da => da.BranchId == branchId.Value);

        if (fromDate.HasValue)
            query = query.Where(da => da.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(da => da.CreatedAt <= toDate.Value);

        query = sortBy.ToLower() switch
        {
            "amount" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(da => da.Amount)
                : query.OrderBy(da => da.Amount),
            Roles.Deliveryman => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(da => da.Deliveryman.Name)
                : query.OrderBy(da => da.Deliveryman.Name),
            "createdat" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(da => da.CreatedAt)
                : query.OrderBy(da => da.CreatedAt),
            _ => query.OrderByDescending(da => da.CreatedAt)
        };

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<DeliverymanAdvance?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.DeliverymanAdvances
            .AsNoTracking()
            .Include(da => da.Deliveryman)
            .Include(da => da.Creator)
            .Include(da => da.Branch)
            .Include(da => da.Bank)
            .FirstOrDefaultAsync(da => da.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<DeliverymanAdvance>> GetByDeliverymanIdAsync(
        int deliverymanId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.DeliverymanAdvances
            .AsNoTracking()
            .Include(da => da.Creator)
            .Include(da => da.Branch)
            .Where(da => da.DeliverymanId == deliverymanId);

        if (fromDate.HasValue)
            query = query.Where(da => da.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(da => da.CreatedAt <= toDate.Value);

        return await query
            .OrderByDescending(da => da.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<DeliverymanAdvance> CreateAsync(DeliverymanAdvance advance, CancellationToken cancellationToken = default)
    {
        _context.DeliverymanAdvances.Add(advance);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(advance.Id, cancellationToken) ?? advance;
    }

    public async Task<DeliverymanAdvance> UpdateAsync(DeliverymanAdvance advance, CancellationToken cancellationToken = default)
    {
        _context.DeliverymanAdvances.Update(advance);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(advance.Id, cancellationToken) ?? advance;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var advance = await _context.DeliverymanAdvances.FindAsync([id], cancellationToken);
        if (advance == null)
            return false;

        _context.DeliverymanAdvances.Remove(advance);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.DeliverymanAdvances.AnyAsync(da => da.Id == id, cancellationToken);
    }

    public async Task<decimal> GetTotalAdvancesForDateAsync(int deliverymanId, DateTime date, CancellationToken cancellationToken = default)
    {
        var total = await _context.DeliverymanAdvances
            .Where(da =>
                da.DeliverymanId == deliverymanId &&
                da.CreatedAt.Date == date.Date)
            .SumAsync(da => (decimal?)da.Amount, cancellationToken);

        return total ?? 0;
    }

    public async Task<decimal> GetTotalAdvancesByDeliverymanAsync(
        int deliverymanId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.DeliverymanAdvances
            .Where(da => da.DeliverymanId == deliverymanId);

        if (fromDate.HasValue)
            query = query.Where(da => da.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(da => da.CreatedAt <= toDate.Value);

        var total = await query.SumAsync(da => (decimal?)da.Amount, cancellationToken);
        return total ?? 0;
    }

    public async Task<decimal> GetTotalAdvancesForSettlementCycleAsync(
        int deliverymanId,
        DateTime dayFromUtc,
        DateTime dayToUtc,
        DateTime? lastLiquidationAtUtc,
        bool useSettlementCycle,
        CancellationToken cancellationToken = default)
    {
        var query = _context.DeliverymanAdvances
            .Where(da =>
                da.DeliverymanId == deliverymanId
                && da.CreatedAt >= dayFromUtc
                && da.CreatedAt <= dayToUtc);

        if (useSettlementCycle && lastLiquidationAtUtc.HasValue)
            query = query.Where(da => da.CreatedAt > lastLiquidationAtUtc.Value);

        var total = await query.SumAsync(da => (decimal?)da.Amount, cancellationToken);
        return total ?? 0;
    }

    public async Task<int> GetCountByDeliverymanAsync(
        int deliverymanId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.DeliverymanAdvances
            .Where(da => da.DeliverymanId == deliverymanId);

        if (fromDate.HasValue)
            query = query.Where(da => da.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(da => da.CreatedAt <= toDate.Value);

        return await query.CountAsync(cancellationToken);
    }

    public Task<bool> ExistsExpenseOffsetForExpenseHeaderAsync(
        int deliverymanId,
        int expenseHeaderId,
        CancellationToken cancellationToken = default) =>
        _context.DeliverymanAdvances
            .AnyAsync(
                da => da.DeliverymanId == deliverymanId && da.ExpenseHeaderId == expenseHeaderId,
                cancellationToken);
}
