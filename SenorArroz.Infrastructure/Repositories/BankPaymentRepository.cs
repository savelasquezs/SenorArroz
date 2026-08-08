// SenorArroz.Infrastructure/Repositories/BankPaymentRepository.cs
using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Common;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Infrastructure.Repositories;

public class BankPaymentRepository : IBankPaymentRepository
{
    private readonly ApplicationDbContext _context;

    public BankPaymentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>Npgsql exige Kind=Utc en parámetros para <c>timestamptz</c>.</summary>
    private static DateTime AsUtcQueryParameter(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    public async Task<PagedResult<BankPayment>> GetPagedAsync(
        int? orderId = null,
        int? bankId = null,
        bool? verified = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "createdAt",
        string sortOrder = "desc",
        int? restrictToBankBranchId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.BankPayments
            .AsNoTracking()
            .Include(bp => bp.Order)
            .Include(bp => bp.Bank)
            .ThenInclude(b => b.Branch)
            .AsQueryable();

        if (restrictToBankBranchId.HasValue)
            query = query.Where(bp => bp.Bank.BranchId == restrictToBankBranchId.Value);

        if (orderId.HasValue)
            query = query.Where(bp => bp.OrderId == orderId.Value);

        if (bankId.HasValue)
            query = query.Where(bp => bp.BankId == bankId.Value);

        if (verified.HasValue)
        {
            if (verified.Value)
                query = query.Where(bp => bp.IsVerified);
            else
                query = query.Where(bp => !bp.IsVerified);
        }

        if (fromDate.HasValue)
        {
            var fromUtc = AsUtcQueryParameter(fromDate.Value);
            query = query.Where(bp => bp.CreatedAt >= fromUtc);
        }

        if (toDate.HasValue)
        {
            var toUtc = AsUtcQueryParameter(toDate.Value);
            query = query.Where(bp => bp.CreatedAt <= toUtc);
        }

        query = sortBy.ToLower() switch
        {
            "amount" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(bp => bp.Amount) : query.OrderBy(bp => bp.Amount),
            "createdat" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(bp => bp.CreatedAt) : query.OrderBy(bp => bp.CreatedAt),
            "bank" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(bp => bp.Bank.Name) : query.OrderBy(bp => bp.Bank.Name),
            "order" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(bp => bp.OrderId) : query.OrderBy(bp => bp.OrderId),
            _ => query.OrderByDescending(bp => bp.CreatedAt)
        };

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<IEnumerable<BankPayment>> GetByOrderIdAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await _context.BankPayments
            .AsNoTracking()
            .Include(bp => bp.Bank)
            .ThenInclude(b => b.Branch)
            .Where(bp => bp.OrderId == orderId)
            .OrderBy(bp => bp.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<BankPayment>> GetByBankIdAsync(int bankId, CancellationToken cancellationToken = default)
    {
        return await _context.BankPayments
            .AsNoTracking()
            .Include(bp => bp.Order)
            .Where(bp => bp.BankId == bankId)
            .OrderByDescending(bp => bp.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<BankPayment>> GetUnverifiedAsync(CancellationToken cancellationToken = default)
    {
        return await _context.BankPayments
            .AsNoTracking()
            .Include(bp => bp.Bank)
            .ThenInclude(b => b.Branch)
            .Include(bp => bp.Order)
            .Where(bp => !bp.IsVerified)
            .OrderBy(bp => bp.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<BankPayment?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.BankPayments
            .AsNoTracking()
            .Include(bp => bp.Order)
            .Include(bp => bp.Bank)
            .ThenInclude(b => b.Branch)
            .FirstOrDefaultAsync(bp => bp.Id == id, cancellationToken);
    }

    public async Task<BankPayment> CreateAsync(BankPayment bankPayment, CancellationToken cancellationToken = default)
    {
        _context.BankPayments.Add(bankPayment);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(bankPayment.Id, cancellationToken) ?? bankPayment;
    }

    public async Task<BankPayment> UpdateAsync(BankPayment bankPayment, CancellationToken cancellationToken = default)
    {
        _context.BankPayments.Update(bankPayment);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(bankPayment.Id, cancellationToken) ?? bankPayment;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var bankPayment = await _context.BankPayments.FindAsync([id], cancellationToken);
        if (bankPayment == null)
            return false;

        _context.BankPayments.Remove(bankPayment);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.BankPayments.AnyAsync(bp => bp.Id == id, cancellationToken);
    }

    public async Task<bool> VerifyPaymentAsync(int id, CancellationToken cancellationToken = default)
    {
        var bankPayment = await _context.BankPayments.FindAsync([id], cancellationToken);
        if (bankPayment == null)
            return false;

        bankPayment.IsVerified = true;
        bankPayment.VerifiedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UnverifyPaymentAsync(int id, CancellationToken cancellationToken = default)
    {
        var bankPayment = await _context.BankPayments.FindAsync([id], cancellationToken);
        if (bankPayment == null)
            return false;

        bankPayment.IsVerified = false;
        bankPayment.VerifiedAt = null;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> UnverifyPaymentsForBankInPeriodAsync(
        int bankId,
        DateTime fromUtc,
        DateTime toUtc,
        int? restrictToBankBranchId = null,
        CancellationToken cancellationToken = default)
    {
        var from = AsUtcQueryParameter(fromUtc);
        var to = AsUtcQueryParameter(toUtc);

        var query = _context.BankPayments
            .Where(bp => bp.BankId == bankId
                && bp.IsVerified
                && bp.CreatedAt >= from
                && bp.CreatedAt <= to);

        if (restrictToBankBranchId.HasValue)
            query = query.Where(bp => bp.Bank.BranchId == restrictToBankBranchId.Value);

        return await query.ExecuteUpdateAsync(
            updates => updates
                .SetProperty(bp => bp.IsVerified, false)
                .SetProperty(bp => bp.VerifiedAt, (DateTime?)null),
            cancellationToken);
    }

    public async Task<decimal> GetTotalAmountByBankAsync(int bankId, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        var query = _context.BankPayments.Where(bp => bp.BankId == bankId);

        if (fromDate.HasValue)
            query = query.Where(bp => bp.CreatedAt >= AsUtcQueryParameter(fromDate.Value));

        if (toDate.HasValue)
            query = query.Where(bp => bp.CreatedAt <= AsUtcQueryParameter(toDate.Value));

        return await query.SumAsync(bp => bp.Amount, cancellationToken);
    }

    public async Task<decimal> GetTotalAmountByOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await _context.BankPayments
            .Where(bp => bp.OrderId == orderId)
            .SumAsync(bp => bp.Amount, cancellationToken);
    }

    public async Task<int> GetTotalCountByBankAsync(int bankId, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        var query = _context.BankPayments.Where(bp => bp.BankId == bankId);

        if (fromDate.HasValue)
            query = query.Where(bp => bp.CreatedAt >= AsUtcQueryParameter(fromDate.Value));

        if (toDate.HasValue)
            query = query.Where(bp => bp.CreatedAt <= AsUtcQueryParameter(toDate.Value));

        return await query.CountAsync(cancellationToken);
    }

    public async Task<int> GetUnverifiedCountByBankAsync(int bankId, CancellationToken cancellationToken = default)
    {
        return await _context.BankPayments
            .CountAsync(bp => bp.BankId == bankId && !bp.IsVerified, cancellationToken);
    }
}
