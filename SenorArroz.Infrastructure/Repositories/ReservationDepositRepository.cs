using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
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

    public async Task<ReservationDeposit> CreateAsync(ReservationDeposit deposit)
    {
        _context.ReservationDeposits.Add(deposit);
        await _context.SaveChangesAsync();
        return await GetByIdAsync(deposit.Id) ?? deposit;
    }

    public async Task<ReservationDeposit?> GetByIdAsync(int id)
    {
        return await _context.ReservationDeposits
            .Include(d => d.Order)
            .Include(d => d.Branch)
            .Include(d => d.Bank)
            .Include(d => d.App)
            .Include(d => d.ReceivedBy)
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<List<ReservationDeposit>> GetByOrderIdAsync(int orderId)
    {
        return await _context.ReservationDeposits
            .Include(d => d.Bank)
            .Include(d => d.App)
            .Include(d => d.ReceivedBy)
            .Where(d => d.OrderId == orderId)
            .OrderByDescending(d => d.ReceivedAt)
            .ToListAsync();
    }

    public async Task<decimal> GetTotalDepositedByOrderAsync(int orderId)
    {
        return await _context.ReservationDeposits
            .Where(d => d.OrderId == orderId)
            .SumAsync(d => d.Amount);
    }

    public async Task<PagedResult<ReservationDeposit>> GetPagedAsync(
        int branchId,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int? orderId = null,
        int page = 1,
        int pageSize = 20)
    {
        var query = _context.ReservationDeposits
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

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(d => d.ReceivedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<ReservationDeposit>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }
}
