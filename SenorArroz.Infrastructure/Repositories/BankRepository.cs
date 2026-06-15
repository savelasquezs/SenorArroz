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
        string sortOrder = "asc",
        CancellationToken cancellationToken = default)
    {
        var query = _context.Banks
            .AsNoTracking()
            .Include(b => b.Branch)
            .AsQueryable();

        if (branchId.HasValue)
            query = query.Where(b => b.BranchId == branchId.Value);

        if (excludeHiddenBanks)
            query = query.Where(b => b.Type != BankType.CashVault && b.Type != BankType.RealVault);

        if (!string.IsNullOrWhiteSpace(name))
            query = query.Where(b => EF.Functions.ILike(b.Name, $"%{name}%"));

        if (active.HasValue)
            query = query.Where(b => b.Active == active.Value);

        query = sortBy.ToLower() switch
        {
            "name" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(b => b.Name) : query.OrderBy(b => b.Name),
            "branch" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(b => b.Branch.Name) : query.OrderBy(b => b.Branch.Name),
            "createdat" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(b => b.CreatedAt) : query.OrderBy(b => b.CreatedAt),
            _ => query.OrderBy(b => b.Name)
        };

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<IEnumerable<Bank>> GetByBranchIdAsync(int branchId, bool excludeHiddenBanks = false, CancellationToken cancellationToken = default)
    {
        var query = _context.Banks
            .AsNoTracking()
            .Include(b => b.Branch)
            .Where(b => b.BranchId == branchId);

        if (excludeHiddenBanks)
            query = query.Where(b => b.Type != BankType.CashVault && b.Type != BankType.RealVault);

        return await query.OrderBy(b => b.Name).ToListAsync(cancellationToken);
    }

    public async Task<Bank?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Banks
            .AsNoTracking()
            .Include(b => b.Branch)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }

    public async Task<Bank?> GetByIdWithAppsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Banks
            .AsNoTracking()
            .Include(b => b.Branch)
            .Include(b => b.Apps.Where(a => a.Active))
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }

    public async Task<Bank> CreateAsync(Bank bank, CancellationToken cancellationToken = default)
    {
        _context.Banks.Add(bank);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(bank.Id, cancellationToken) ?? bank;
    }

    public async Task<Bank> UpdateAsync(Bank bank, CancellationToken cancellationToken = default)
    {
        _context.Banks.Update(bank);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(bank.Id, cancellationToken) ?? bank;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var bank = await _context.Banks.FindAsync([id], cancellationToken);
        if (bank == null)
            return false;

        var hasApps = await _context.Apps.AnyAsync(a => a.BankId == id, cancellationToken);
        var hasBankPayments = await _context.BankPayments.AnyAsync(bp => bp.BankId == id, cancellationToken);
        var hasReservationDeposits = await _context.ReservationDeposits.AnyAsync(d => d.BankId == id, cancellationToken);

        if (hasApps || hasBankPayments || hasReservationDeposits)
        {
            bank.Active = false;
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }

        _context.Banks.Remove(bank);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Banks.AnyAsync(b => b.Id == id, cancellationToken);
    }

    public async Task<bool> NameExistsInBranchAsync(string name, int branchId, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        return await _context.Banks.AnyAsync(b =>
            b.Name.ToLower() == name.ToLower() &&
            b.BranchId == branchId &&
            (!excludeId.HasValue || b.Id != excludeId.Value), cancellationToken);
    }

    public async Task<int> GetTotalAppsAsync(int bankId, CancellationToken cancellationToken = default)
    {
        return await _context.Apps.CountAsync(a => a.BankId == bankId, cancellationToken);
    }

    public async Task<int> GetActiveAppsAsync(int bankId, CancellationToken cancellationToken = default)
    {
        return await _context.Apps.CountAsync(a => a.BankId == bankId && a.Active, cancellationToken);
    }

    public async Task<decimal> GetTotalBankPaymentsAsync(int bankId, CancellationToken cancellationToken = default)
    {
        return await _context.BankPayments
            .Where(bp => bp.BankId == bankId && !bp.SourceReservationDepositId.HasValue)
            .SumAsync(bp => bp.Amount, cancellationToken);
    }

    public async Task<decimal> GetTotalReservationDepositsAsync(int bankId, DateTime? asOf = null, CancellationToken cancellationToken = default)
    {
        var query = _context.ReservationDeposits.Where(d => d.BankId == bankId);
        if (asOf.HasValue)
        {
            var utc = DateTime.SpecifyKind(asOf.Value, DateTimeKind.Utc);
            query = query.Where(d => d.ReceivedAt <= utc);
        }

        return await query.SumAsync(d => (decimal?)d.Amount, cancellationToken) ?? 0m;
    }

    public async Task<decimal> GetTotalExpenseBankPaymentsAsync(int bankId, CancellationToken cancellationToken = default)
    {
        return await _context.ExpenseBankPayments
            .Where(ebp => ebp.BankId == bankId)
            .SumAsync(ebp => ebp.Amount, cancellationToken);
    }

    public async Task<decimal> GetTotalOutgoingTransfersAsync(int bankId, DateTime? asOf = null, CancellationToken cancellationToken = default)
    {
        var query = _context.BankTransfers.Where(bt => bt.FromBankId == bankId);
        if (asOf.HasValue)
        {
            var utc = DateTime.SpecifyKind(asOf.Value, DateTimeKind.Utc);
            query = query.Where(bt => bt.CreatedAt <= utc);
        }
        return await query.SumAsync(bt => bt.Amount, cancellationToken);
    }

    public async Task<decimal> GetTotalIncomingTransfersAsync(int bankId, DateTime? asOf = null, CancellationToken cancellationToken = default)
    {
        var query = _context.BankTransfers.Where(bt => bt.ToBankId == bankId);
        if (asOf.HasValue)
        {
            var utc = DateTime.SpecifyKind(asOf.Value, DateTimeKind.Utc);
            query = query.Where(bt => bt.CreatedAt <= utc);
        }
        return await query.SumAsync(bt => bt.Amount, cancellationToken);
    }

    public async Task<decimal> GetCurrentBalanceAsync(int bankId, CancellationToken cancellationToken = default)
    {
        var totalIncome = await GetTotalBankPaymentsAsync(bankId, cancellationToken);
        var reservationDeposits = await GetTotalReservationDepositsAsync(bankId, cancellationToken: cancellationToken);
        var totalExpenses = await GetTotalExpenseBankPaymentsAsync(bankId, cancellationToken);
        var outgoing = await GetTotalOutgoingTransfersAsync(bankId, cancellationToken: cancellationToken);
        var incoming = await GetTotalIncomingTransfersAsync(bankId, cancellationToken: cancellationToken);
        var deliverymanTransferIn = await GetTotalDeliverymanBankTransferInAsync(bankId, cancellationToken: cancellationToken);
        return totalIncome + reservationDeposits - totalExpenses - outgoing + incoming + deliverymanTransferIn;
    }

    public async Task<decimal> GetBalanceAsOfAsync(int bankId, DateTime asOf, CancellationToken cancellationToken = default)
    {
        var utc = DateTime.SpecifyKind(asOf, DateTimeKind.Utc);
        var totalIncome = await _context.BankPayments
            .Where(bp => bp.BankId == bankId
                && !bp.SourceReservationDepositId.HasValue
                && bp.CreatedAt <= utc)
            .SumAsync(bp => bp.Amount, cancellationToken);
        var reservationDeposits = await GetTotalReservationDepositsAsync(bankId, utc, cancellationToken);
        var totalExpenses = await _context.ExpenseBankPayments
            .Where(ebp => ebp.BankId == bankId && ebp.CreatedAt <= utc)
            .SumAsync(ebp => ebp.Amount, cancellationToken);
        var outgoing = await GetTotalOutgoingTransfersAsync(bankId, utc, cancellationToken);
        var incoming = await GetTotalIncomingTransfersAsync(bankId, utc, cancellationToken);
        var deliverymanTransferIn = await GetTotalDeliverymanBankTransferInAsync(bankId, utc, cancellationToken);
        return totalIncome + reservationDeposits - totalExpenses - outgoing + incoming + deliverymanTransferIn;
    }

    /// <inheritdoc />
    public async Task<decimal> GetTotalDeliverymanBankTransferInAsync(int bankId, DateTime? asOf = null, CancellationToken cancellationToken = default)
    {
        var query = _context.DeliverymanAdvances.Where(a =>
            a.BankId == bankId && a.PaymentMethod == DeliverymanAdvancePaymentMethod.BankTransfer);
        if (asOf.HasValue)
        {
            var utc = DateTime.SpecifyKind(asOf.Value, DateTimeKind.Utc);
            query = query.Where(a => a.CreatedAt <= utc);
        }

        return await query.SumAsync(a => (decimal?)a.Amount, cancellationToken) ?? 0m;
    }

    public async Task<decimal> GetTotalBankPaymentsInPeriodAsync(int bankId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var from = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);
        return await _context.BankPayments
            .Where(bp => bp.BankId == bankId
                && !bp.SourceReservationDepositId.HasValue
                && bp.CreatedAt >= from && bp.CreatedAt <= to)
            .SumAsync(bp => bp.Amount, cancellationToken);
    }

    public async Task<decimal> GetTotalReservationDepositsInPeriodAsync(int bankId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var from = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);
        return await _context.ReservationDeposits
            .Where(d => d.BankId == bankId && d.ReceivedAt >= from && d.ReceivedAt <= to)
            .SumAsync(d => (decimal?)d.Amount, cancellationToken) ?? 0m;
    }

    public async Task<decimal> GetTotalExpenseBankPaymentsInPeriodAsync(int bankId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var from = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);
        return await _context.ExpenseBankPayments
            .Where(ebp => ebp.BankId == bankId && ebp.CreatedAt >= from && ebp.CreatedAt <= to)
            .SumAsync(ebp => ebp.Amount, cancellationToken);
    }

    public async Task<decimal> GetTotalOutgoingTransfersInPeriodAsync(int bankId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var from = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);
        return await _context.BankTransfers
            .Where(bt => bt.FromBankId == bankId && bt.CreatedAt >= from && bt.CreatedAt <= to)
            .SumAsync(bt => bt.Amount, cancellationToken);
    }

    public async Task<decimal> GetTotalIncomingTransfersInPeriodAsync(int bankId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var from = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);
        return await _context.BankTransfers
            .Where(bt => bt.ToBankId == bankId && bt.CreatedAt >= from && bt.CreatedAt <= to)
            .SumAsync(bt => bt.Amount, cancellationToken);
    }

    public async Task<decimal> GetTotalDeliverymanBankTransferInPeriodAsync(int bankId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var from = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);
        return await _context.DeliverymanAdvances
            .Where(a => a.BankId == bankId
                && a.PaymentMethod == DeliverymanAdvancePaymentMethod.BankTransfer
                && a.CreatedAt >= from && a.CreatedAt <= to)
            .SumAsync(a => (decimal?)a.Amount, cancellationToken) ?? 0m;
    }
}
