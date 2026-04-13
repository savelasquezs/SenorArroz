using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Common;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Infrastructure.Repositories;

public class CashRegisterClosureRepository : ICashRegisterClosureRepository
{
    private readonly ApplicationDbContext _context;

    public CashRegisterClosureRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CashRegisterClosure?> GetLastByBranchAsync(int branchId, CancellationToken cancellationToken = default)
    {
        return await _context.CashRegisterClosures
            .AsNoTracking()
            .Include(c => c.Branch)
            .Include(c => c.CreatedBy)
            .Include(c => c.BankReconciliations)
                .ThenInclude(br => br.Bank)
            .Include(c => c.InformalLoans)
            .Where(c => c.BranchId == branchId)
            .OrderByDescending(c => c.ClosedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<CashRegisterClosure> CreateAsync(CashRegisterClosure closure, CancellationToken cancellationToken = default)
    {
        _context.CashRegisterClosures.Add(closure);
        await _context.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(closure.Id, cancellationToken) ?? closure;
    }

    public async Task<PagedResult<CashRegisterClosure>> GetPagedAsync(
        int? branchId,
        DateTime? fromDate,
        DateTime? toDate,
        int page = 1,
        int pageSize = 10,
        string sortBy = "closedAt",
        string sortOrder = "desc",
        CancellationToken cancellationToken = default)
    {
        var query = _context.CashRegisterClosures
            .AsNoTracking()
            .Include(c => c.Branch)
            .Include(c => c.CreatedBy)
            .Include(c => c.BankReconciliations)
                .ThenInclude(br => br.Bank)
            .Include(c => c.InformalLoans)
            .AsQueryable();

        if (branchId.HasValue)
            query = query.Where(c => c.BranchId == branchId.Value);

        if (fromDate.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Utc);
            query = query.Where(c => c.ClosedAt >= fromUtc);
        }
        if (toDate.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(toDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            query = query.Where(c => c.ClosedAt <= toUtc);
        }

        query = sortBy.ToLower() switch
        {
            "closedat" or "date" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(c => c.ClosedAt)
                : query.OrderBy(c => c.ClosedAt),
            "id" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(c => c.Id)
                : query.OrderBy(c => c.Id),
            _ => query.OrderByDescending(c => c.ClosedAt)
        };

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<CashRegisterClosure?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.CashRegisterClosures
            .AsNoTracking()
            .Include(c => c.Branch)
            .Include(c => c.CreatedBy)
            .Include(c => c.BankReconciliations)
                .ThenInclude(br => br.Bank)
            .Include(c => c.InformalLoans)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }
}
