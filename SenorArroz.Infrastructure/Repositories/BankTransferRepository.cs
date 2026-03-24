using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Infrastructure.Repositories;

public class BankTransferRepository : IBankTransferRepository
{
    private readonly ApplicationDbContext _context;

    public BankTransferRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<BankTransfer> CreateAsync(BankTransfer transfer)
    {
        _context.BankTransfers.Add(transfer);
        await _context.SaveChangesAsync();
        return await GetByIdAsync(transfer.Id) ?? transfer;
    }

    public async Task<BankTransfer?> GetByIdAsync(int id)
    {
        return await _context.BankTransfers
            .Include(bt => bt.FromBank)
            .Include(bt => bt.ToBank)
            .Include(bt => bt.CreatedBy)
            .FirstOrDefaultAsync(bt => bt.Id == id);
    }

    public async Task<PagedResult<BankTransfer>> GetPagedAsync(
        int? branchId,
        int? fromBankId,
        int? toBankId,
        DateTime? fromDate,
        DateTime? toDate,
        int page,
        int pageSize,
        string sortBy,
        string sortOrder)
    {
        var query = _context.BankTransfers
            .Include(bt => bt.FromBank)
            .Include(bt => bt.ToBank)
            .Include(bt => bt.CreatedBy)
            .AsQueryable();

        if (branchId.HasValue)
        {
            query = query.Where(bt => bt.FromBank.BranchId == branchId.Value || bt.ToBank.BranchId == branchId.Value);
        }
        if (fromBankId.HasValue)
            query = query.Where(bt => bt.FromBankId == fromBankId.Value);
        if (toBankId.HasValue)
            query = query.Where(bt => bt.ToBankId == toBankId.Value);
        if (fromDate.HasValue)
        {
            var fromUtc = fromDate.Value.Kind == DateTimeKind.Utc
                ? fromDate.Value
                : fromDate.Value.Kind == DateTimeKind.Local
                    ? fromDate.Value.ToUniversalTime()
                    : DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Utc);
            query = query.Where(bt => bt.CreatedAt >= fromUtc);
        }

        if (toDate.HasValue)
        {
            var toUtc = toDate.Value.Kind == DateTimeKind.Utc
                ? toDate.Value
                : toDate.Value.Kind == DateTimeKind.Local
                    ? toDate.Value.ToUniversalTime()
                    : DateTime.SpecifyKind(toDate.Value, DateTimeKind.Utc);
            query = query.Where(bt => bt.CreatedAt <= toUtc);
        }

        var totalCount = await query.CountAsync();

        query = sortBy.ToLower() switch
        {
            "amount" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(bt => bt.Amount) : query.OrderBy(bt => bt.Amount),
            "createdat" or "date" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(bt => bt.CreatedAt) : query.OrderBy(bt => bt.CreatedAt),
            "frombank" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(bt => bt.FromBank.Name) : query.OrderBy(bt => bt.FromBank.Name),
            "tobank" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(bt => bt.ToBank.Name) : query.OrderBy(bt => bt.ToBank.Name),
            _ => query.OrderByDescending(bt => bt.CreatedAt)
        };

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<BankTransfer>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }
}
