using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Common;
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

    public async Task<BankTransfer> CreateAsync(BankTransfer transfer, CancellationToken cancellationToken = default)
    {
        _context.BankTransfers.Add(transfer);
        await _context.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(transfer.Id, cancellationToken) ?? transfer;
    }

    private async Task<BankTransfer?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.BankTransfers
            .AsNoTracking()
            .Include(bt => bt.FromBank)
            .Include(bt => bt.ToBank)
            .Include(bt => bt.CreatedBy)
            .FirstOrDefaultAsync(bt => bt.Id == id, cancellationToken);
    }

    public async Task<PagedResult<BankTransfer>> GetPagedAsync(
        int? branchId = null,
        int? fromBankId = null,
        int? toBankId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "createdAt",
        string sortOrder = "desc",
        CancellationToken cancellationToken = default)
    {
        var query = _context.BankTransfers
            .AsNoTracking()
            .Include(bt => bt.FromBank)
            .Include(bt => bt.ToBank)
            .Include(bt => bt.CreatedBy)
            .AsQueryable();

        if (branchId.HasValue)
        {
            var bid = branchId.Value;
            query = query.Where(bt =>
                (bt.FromBankId != null && bt.FromBank!.BranchId == bid)
                || (bt.ToBankId != null && bt.ToBank!.BranchId == bid));
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

        query = sortBy.ToLower() switch
        {
            "amount" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(bt => bt.Amount) : query.OrderBy(bt => bt.Amount),
            "createdat" or "date" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(bt => bt.CreatedAt) : query.OrderBy(bt => bt.CreatedAt),
            "frombank" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(bt => bt.FromBank != null ? bt.FromBank.Name : "")
                : query.OrderBy(bt => bt.FromBank != null ? bt.FromBank.Name : ""),
            "tobank" => sortOrder.ToLower() == "desc"
                ? query.OrderByDescending(bt => bt.ToBank != null ? bt.ToBank.Name : "")
                : query.OrderBy(bt => bt.ToBank != null ? bt.ToBank.Name : ""),
            _ => query.OrderByDescending(bt => bt.CreatedAt)
        };

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }
}
