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
        int? restrictToBankBranchId = null)
    {
        var query = _context.BankPayments
            .Include(bp => bp.Order)
            .Include(bp => bp.Bank)
            .ThenInclude(b => b.Branch)
            .AsQueryable();

        if (restrictToBankBranchId.HasValue)
        {
            query = query.Where(bp => bp.Bank.BranchId == restrictToBankBranchId.Value);
        }

        // Order filter
        if (orderId.HasValue)
        {
            query = query.Where(bp => bp.OrderId == orderId.Value);
        }

        // Bank filter
        if (bankId.HasValue)
        {
            query = query.Where(bp => bp.BankId == bankId.Value);
        }

        // Verification filter
        if (verified.HasValue)
        {
            if (verified.Value)
            {
                query = query.Where(bp => bp.VerifiedAt.HasValue);
            }
            else
            {
                query = query.Where(bp => !bp.VerifiedAt.HasValue);
            }
        }

        // Date range filter (instantes UTC; el handler suele normalizar con ColombiaTimeHelper)
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

        // Sorting
        query = sortBy.ToLower() switch
        {
            "amount" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(bp => bp.Amount) : query.OrderBy(bp => bp.Amount),
            "createdat" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(bp => bp.CreatedAt) : query.OrderBy(bp => bp.CreatedAt),
            "bank" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(bp => bp.Bank.Name) : query.OrderBy(bp => bp.Bank.Name),
            "order" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(bp => bp.OrderId) : query.OrderBy(bp => bp.OrderId),
            _ => query.OrderByDescending(bp => bp.CreatedAt)
        };

        return await query.ToPagedResultAsync(page, pageSize);
    }

    public async Task<IEnumerable<BankPayment>> GetByOrderIdAsync(int orderId)
    {
        return await _context.BankPayments
            .Include(bp => bp.Bank)
            .ThenInclude(b => b.Branch)
            .Where(bp => bp.OrderId == orderId)
            .OrderBy(bp => bp.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<BankPayment>> GetByBankIdAsync(int bankId)
    {
        return await _context.BankPayments
            .Include(bp => bp.Order)
            .Where(bp => bp.BankId == bankId)
            .OrderByDescending(bp => bp.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<BankPayment>> GetUnverifiedAsync()
    {
        return await _context.BankPayments
            .Include(bp => bp.Bank)
            .ThenInclude(b => b.Branch)
            .Include(bp => bp.Order)
            .Where(bp => !bp.VerifiedAt.HasValue)
            .OrderBy(bp => bp.CreatedAt)
            .ToListAsync();
    }

    public async Task<BankPayment?> GetByIdAsync(int id)
    {
        return await _context.BankPayments
            .Include(bp => bp.Order)
            .Include(bp => bp.Bank)
            .ThenInclude(b => b.Branch)
            .FirstOrDefaultAsync(bp => bp.Id == id);
    }

    public async Task<BankPayment> CreateAsync(BankPayment bankPayment)
    {
        _context.BankPayments.Add(bankPayment);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(bankPayment.Id) ?? bankPayment;
    }

    public async Task<BankPayment> UpdateAsync(BankPayment bankPayment)
    {
        _context.BankPayments.Update(bankPayment);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(bankPayment.Id) ?? bankPayment;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var bankPayment = await _context.BankPayments.FindAsync(id);
        if (bankPayment == null)
            return false;

        _context.BankPayments.Remove(bankPayment);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(int id)
    {
        return await _context.BankPayments.AnyAsync(bp => bp.Id == id);
    }

    // Verification methods
    public async Task<bool> VerifyPaymentAsync(int id)
    {
        var bankPayment = await _context.BankPayments.FindAsync(id);
        if (bankPayment == null)
            return false;

        bankPayment.IsVerified = true;  // El trigger en PostgreSQL establecerá verified_at automáticamente
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UnverifyPaymentAsync(int id)
    {
        var bankPayment = await _context.BankPayments.FindAsync(id);
        if (bankPayment == null)
            return false;

        bankPayment.IsVerified = false;  // El trigger en PostgreSQL establecerá verified_at = NULL automáticamente
        await _context.SaveChangesAsync();
        return true;
    }

    // Statistics
    public async Task<decimal> GetTotalAmountByBankAsync(int bankId, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var query = _context.BankPayments.Where(bp => bp.BankId == bankId);

        if (fromDate.HasValue)
            query = query.Where(bp => bp.CreatedAt >= AsUtcQueryParameter(fromDate.Value));

        if (toDate.HasValue)
            query = query.Where(bp => bp.CreatedAt <= AsUtcQueryParameter(toDate.Value));

        return await query.SumAsync(bp => bp.Amount);
    }

    public async Task<decimal> GetTotalAmountByOrderAsync(int orderId)
    {
        return await _context.BankPayments
            .Where(bp => bp.OrderId == orderId)
            .SumAsync(bp => bp.Amount);
    }

    public async Task<int> GetTotalCountByBankAsync(int bankId, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var query = _context.BankPayments.Where(bp => bp.BankId == bankId);

        if (fromDate.HasValue)
            query = query.Where(bp => bp.CreatedAt >= AsUtcQueryParameter(fromDate.Value));

        if (toDate.HasValue)
            query = query.Where(bp => bp.CreatedAt <= AsUtcQueryParameter(toDate.Value));

        return await query.CountAsync();
    }

    public async Task<int> GetUnverifiedCountByBankAsync(int bankId)
    {
        return await _context.BankPayments
            .CountAsync(bp => bp.BankId == bankId && !bp.VerifiedAt.HasValue);
    }
}
