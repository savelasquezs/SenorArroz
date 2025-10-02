// SenorArroz.Infrastructure/Repositories/BankRepository.cs
using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
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
        int page = 1,
        int pageSize = 10,
        string sortBy = "name",
        string sortOrder = "asc")
    {
        var query = _context.Banks
            .Include(b => b.Branch)
            .AsQueryable();

        // Branch filter
        if (branchId.HasValue)
        {
            query = query.Where(b => b.BranchId == branchId.Value);
        }

        // Name filter
        if (!string.IsNullOrWhiteSpace(name))
        {
            query = query.Where(b => b.Name.ToLower().Contains(name.ToLower()));
        }

        // Active filter
        if (active.HasValue)
        {
            query = query.Where(b => b.Active == active.Value);
        }

        // Total count
        var totalCount = await query.CountAsync();

        // Sorting
        query = sortBy.ToLower() switch
        {
            "name" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(b => b.Name) : query.OrderBy(b => b.Name),
            "branch" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(b => b.Branch.Name) : query.OrderBy(b => b.Branch.Name),
            "createdat" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(b => b.CreatedAt) : query.OrderBy(b => b.CreatedAt),
            _ => query.OrderBy(b => b.Name)
        };

        // Pagination
        var banks = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Bank>
        {
            Items = banks,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<IEnumerable<Bank>> GetByBranchIdAsync(int branchId)
    {
        return await _context.Banks
            .Include(b => b.Branch)
            .Where(b => b.BranchId == branchId)
            .OrderBy(b => b.Name)
            .ToListAsync();
    }

    public async Task<Bank?> GetByIdAsync(int id)
    {
        return await _context.Banks
            .Include(b => b.Branch)
            .FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<Bank?> GetByIdWithAppsAsync(int id)
    {
        return await _context.Banks
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

    public async Task<decimal> GetCurrentBalanceAsync(int bankId)
    {
        var totalIncome = await GetTotalBankPaymentsAsync(bankId);
        var totalExpenses = await GetTotalExpenseBankPaymentsAsync(bankId);
        return totalIncome - totalExpenses;
    }
}
