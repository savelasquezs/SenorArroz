using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Common;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Infrastructure.Repositories;

public class ReservationDepositRepository : IReservationDepositRepository
{
    private readonly ApplicationDbContext _context;

    public ReservationDepositRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ReservationDeposit> CreateAsync(ReservationDeposit deposit, CancellationToken cancellationToken = default)
    {
        _context.ReservationDeposits.Add(deposit);
        await _context.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(deposit.Id, cancellationToken) ?? deposit;
    }

    public async Task<ReservationDeposit?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.ReservationDeposits
            .AsNoTracking()
            .Include(d => d.Order)
            .Include(d => d.Branch)
            .Include(d => d.Bank)
            .Include(d => d.App)
            .Include(d => d.ReceivedBy)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<List<ReservationDeposit>> GetByOrderIdAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await _context.ReservationDeposits
            .AsNoTracking()
            .Include(d => d.Bank)
            .Include(d => d.App)
            .Include(d => d.ReceivedBy)
            .Where(d => d.OrderId == orderId)
            .OrderByDescending(d => d.ReceivedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<decimal> GetTotalDepositedByOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await _context.ReservationDeposits
            .Where(d => d.OrderId == orderId)
            .SumAsync(d => d.Amount, cancellationToken);
    }

    public async Task<int> DeleteByOrderIdAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await _context.ReservationDeposits
            .Where(d => d.OrderId == orderId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<PagedResult<ReservationDeposit>> GetPagedAsync(
        int branchId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int? orderId = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ReservationDeposits
            .AsNoTracking()
            .Include(d => d.Order)
            .Include(d => d.Bank)
            .Include(d => d.App)
            .Include(d => d.ReceivedBy)
            .Where(d => d.BranchId == branchId)
            .AsQueryable();

        if (orderId.HasValue)
            query = query.Where(d => d.OrderId == orderId.Value);

        if (fromDate.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Utc);
            query = query.Where(d => d.ReceivedAt >= fromUtc);
        }

        if (toDate.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(toDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            query = query.Where(d => d.ReceivedAt <= toUtc);
        }

        query = query.OrderByDescending(d => d.ReceivedAt);

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }
}
