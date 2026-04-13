// SenorArroz.Infrastructure/Repositories/BankRepository.cs
using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Common;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Infrastructure.Repositories;

public class BankRepository : IBankRepository
{
    private readonly ApplicationDbContext _context;

    public BankRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<Bank>> GetPagedAsync(
        int? branchId = null,
        string? name = null,
        bool? active = null,
        bool excludeHiddenBanks = false,
        int page = 1,
        int pageSize = 10,
        string sortBy = "name",
        string sortOrder = "asc")
    {
        var query = _context.Banks
            .AsNoTracking()
            .Include(b => b.Branch)
            .AsQueryable();

        // Branch filter
        if (branchId.HasValue)
        {
            query = query.Where(b => b.BranchId == branchId.Value);
        }

        // Exclude hidden banks (CashVault, RealVault) for non-Admin/Superadmin
        if (excludeHiddenBanks)
        {
            query = query.Where(b => b.Type != Domain.Enums.BankType.CashVault && b.Type != Domain.Enums.BankType.RealVault);
        }

        // Name filter
        if (!string.IsNullOrWhiteSpace(name))
        {
            query = query.Where(b => EF.Functions.ILike(b.Name, $"%{name}%"));
        }

        // Active filter
        if (active.HasValue)
        {
            query = query.Where(b => b.Active == active.Value);
        }

        // Sorting
        query = sortBy.ToLower() switch
        {
            "name" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(b => b.Name) : query.OrderBy(b => b.Name),
            "branch" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(b => b.Branch.Name) : query.OrderBy(b => b.Branch.Name),
            "createdat" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(b => b.CreatedAt) : query.OrderBy(b => b.CreatedAt),
            _ => query.OrderBy(b => b.Name)
        };

        return await query.ToPagedResultAsync(page, pageSize);
    }

    public async Task<IEnumerable<Bank>> GetByBranchIdAsync(int branchId, bool excludeHiddenBanks = false)
    {
        var query = _context.Banks
            .AsNoTracking()
            .Include(b => b.Branch)
            .Where(b => b.BranchId == branchId);

        if (excludeHiddenBanks)
        {
            query = query.Where(b => b.Type != Domain.Enums.BankType.CashVault && b.Type != Domain.Enums.BankType.RealVault);
        }

        return await query.OrderBy(b => b.Name).ToListAsync();
    }

    public async Task<Bank?> GetByIdAsync(int id)
    {
        return await _context.Banks
            .AsNoTracking()
            .Include(b => b.Branch)
            .FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<Bank?> GetByIdWithAppsAsync(int id)
    {
        return await _context.Banks
            .AsNoTracking()
            .Include(b => b.Branch)
            .Include(b => b.Apps.Where(a => a.Active))
            .FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<Bank> CreateAsync(Bank bank)
    {
        _context.Banks.Add(bank);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(bank.Id) ?? bank;
    }

    public async Task<Bank> UpdateAsync(Bank bank)
    {
        _context.Banks.Update(bank);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(bank.Id) ?? bank;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var bank = await _context.Banks.FindAsync(id);
        if (bank == null)
            return false;

        // Check if bank has apps or payments
        var hasApps = await _context.Apps.AnyAsync(a => a.BankId == id);
        var hasBankPayments = await _context.BankPayments.AnyAsync(bp => bp.BankId == id);
        
        if (hasApps || hasBankPayments)
        {
            // Soft delete: just deactivate
            bank.Active = false;
            await _context.SaveChangesAsync();
            return true;
        }

        _context.Banks.Remove(bank);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(int id)
    {
        return await _context.Banks.AnyAsync(b => b.Id == id);
    }

    public async Task<bool> NameExistsInBranchAsync(string name, int branchId, int? excludeId = null)
    {
        return await _context.Banks.AnyAsync(b =>
            b.Name.ToLower() == name.ToLower() &&
            b.BranchId == branchId &&
            (!excludeId.HasValue || b.Id != excludeId.Value));
    }

    // Statistics
    public async Task<int> GetTotalAppsAsync(int bankId)
    {
        return await _context.Apps
            .CountAsync(a => a.BankId == bankId);
    }

    public async Task<int> GetActiveAppsAsync(int bankId)
    {
        return await _context.Apps
            .CountAsync(a => a.BankId == bankId && a.Active);
    }

    public async Task<decimal> GetTotalBankPaymentsAsync(int bankId)
    {
        return await _context.BankPayments
            .Where(bp => bp.BankId == bankId)
            .SumAsync(bp => bp.Amount);
    }

    public async Task<decimal> GetTotalExpenseBankPaymentsAsync(int bankId)
    {
        return await _context.ExpenseBankPayments
            .Where(ebp => ebp.BankId == bankId)
            .SumAsync(ebp => ebp.Amount);
    }

    public async Task<decimal> GetTotalOutgoingTransfersAsync(int bankId, DateTime? asOf = null)
    {
        var query = _context.BankTransfers.Where(bt => bt.FromBankId == bankId);
        if (asOf.HasValue)
        {
            var utc = DateTime.SpecifyKind(asOf.Value, DateTimeKind.Utc);
            query = query.Where(bt => bt.CreatedAt <= utc);
        }
        return await query.SumAsync(bt => bt.Amount);
    }

    public async Task<decimal> GetTotalIncomingTransfersAsync(int bankId, DateTime? asOf = null)
    {
        var query = _context.BankTransfers.Where(bt => bt.ToBankId == bankId);
        if (asOf.HasValue)
        {
            var utc = DateTime.SpecifyKind(asOf.Value, DateTimeKind.Utc);
            query = query.Where(bt => bt.CreatedAt <= utc);
        }
        return await query.SumAsync(bt => bt.Amount);
    }

    public async Task<decimal> GetCurrentBalanceAsync(int bankId)
    {
        var totalIncome = await GetTotalBankPaymentsAsync(bankId);
        var totalExpenses = await GetTotalExpenseBankPaymentsAsync(bankId);
        var outgoing = await GetTotalOutgoingTransfersAsync(bankId);
        var incoming = await GetTotalIncomingTransfersAsync(bankId);
        var deliverymanTransferIn = await GetTotalDeliverymanBankTransferInAsync(bankId, asOf: null);
        return totalIncome - totalExpenses - outgoing + incoming + deliverymanTransferIn;
    }

    public async Task<decimal> GetBalanceAsOfAsync(int bankId, DateTime asOf)
    {
        var utc = DateTime.SpecifyKind(asOf, DateTimeKind.Utc);
        var totalIncome = await _context.BankPayments
            .Where(bp => bp.BankId == bankId && bp.CreatedAt <= utc)
            .SumAsync(bp => bp.Amount);
        var totalExpenses = await _context.ExpenseBankPayments
            .Where(ebp => ebp.BankId == bankId && ebp.CreatedAt <= utc)
            .SumAsync(ebp => ebp.Amount);
        var outgoing = await GetTotalOutgoingTransfersAsync(bankId, utc);
        var incoming = await GetTotalIncomingTransfersAsync(bankId, utc);
        var deliverymanTransferIn = await GetTotalDeliverymanBankTransferInAsync(bankId, utc);
        return totalIncome - totalExpenses - outgoing + incoming + deliverymanTransferIn;
    }

    /// <inheritdoc />
    public async Task<decimal> GetTotalDeliverymanBankTransferInAsync(int bankId, DateTime? asOf = null)
    {
        var query = _context.DeliverymanAdvances.Where(a =>
            a.BankId == bankId && a.PaymentMethod == DeliverymanAdvancePaymentMethod.BankTransfer);
        if (asOf.HasValue)
        {
            var utc = DateTime.SpecifyKind(asOf.Value, DateTimeKind.Utc);
            query = query.Where(a => a.CreatedAt <= utc);
        }

        return await query.SumAsync(a => (decimal?)a.Amount) ?? 0m;
    }

    public async Task<decimal> GetTotalBankPaymentsInPeriodAsync(int bankId, DateTime fromUtc, DateTime toUtc)
    {
        var from = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);
        return await _context.BankPayments
            .Where(bp => bp.BankId == bankId && bp.CreatedAt >= from && bp.CreatedAt <= to)
            .SumAsync(bp => bp.Amount);
    }

    public async Task<decimal> GetTotalExpenseBankPaymentsInPeriodAsync(int bankId, DateTime fromUtc, DateTime toUtc)
    {
        var from = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);
        return await _context.ExpenseBankPayments
            .Where(ebp => ebp.BankId == bankId && ebp.CreatedAt >= from && ebp.CreatedAt <= to)
            .SumAsync(ebp => ebp.Amount);
    }

    public async Task<decimal> GetTotalOutgoingTransfersInPeriodAsync(int bankId, DateTime fromUtc, DateTime toUtc)
    {
        var from = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);
        return await _context.BankTransfers
            .Where(bt => bt.FromBankId == bankId && bt.CreatedAt >= from && bt.CreatedAt <= to)
            .SumAsync(bt => bt.Amount);
    }

    public async Task<decimal> GetTotalIncomingTransfersInPeriodAsync(int bankId, DateTime fromUtc, DateTime toUtc)
    {
        var from = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);
        return await _context.BankTransfers
            .Where(bt => bt.ToBankId == bankId && bt.CreatedAt >= from && bt.CreatedAt <= to)
            .SumAsync(bt => bt.Amount);
    }

    public async Task<decimal> GetTotalDeliverymanBankTransferInPeriodAsync(int bankId, DateTime fromUtc, DateTime toUtc)
    {
        var from = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);
        return await _context.DeliverymanAdvances
            .Where(a => a.BankId == bankId
                && a.PaymentMethod == DeliverymanAdvancePaymentMethod.BankTransfer
                && a.CreatedAt >= from && a.CreatedAt <= to)
            .SumAsync(a => (decimal?)a.Amount) ?? 0m;
    }
}
