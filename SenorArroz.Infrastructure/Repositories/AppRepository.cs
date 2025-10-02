// SenorArroz.Infrastructure/Repositories/AppRepository.cs
using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Infrastructure.Repositories;

public class AppRepository : IAppRepository
{
    private readonly ApplicationDbContext _context;

    public AppRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<App>> GetPagedAsync(
        int? bankId = null,
        string? name = null,
        bool? active = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "name",
        string sortOrder = "asc")
    {
        var query = _context.Apps
            .Include(a => a.Bank)
            .ThenInclude(b => b.Branch)
            .AsQueryable();

        // Bank filter
        if (bankId.HasValue)
        {
            query = query.Where(a => a.BankId == bankId.Value);
        }

        // Name filter
        if (!string.IsNullOrWhiteSpace(name))
        {
            query = query.Where(a => a.Name.ToLower().Contains(name.ToLower()));
        }

        // Active filter
        if (active.HasValue)
        {
            query = query.Where(a => a.Active == active.Value);
        }

        // Total count
        var totalCount = await query.CountAsync();

        // Sorting
        query = sortBy.ToLower() switch
        {
            "name" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(a => a.Name) : query.OrderBy(a => a.Name),
            "bank" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(a => a.Bank.Name) : query.OrderBy(a => a.Bank.Name),
            "createdat" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(a => a.CreatedAt) : query.OrderBy(a => a.CreatedAt),
            _ => query.OrderBy(a => a.Name)
        };

        // Pagination
        var apps = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<App>
        {
            Items = apps,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<IEnumerable<App>> GetByBankIdAsync(int bankId)
    {
        return await _context.Apps
            .Include(a => a.Bank)
            .ThenInclude(b => b.Branch)
            .Where(a => a.BankId == bankId)
            .OrderBy(a => a.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<App>> GetByBranchIdAsync(int branchId)
    {
        return await _context.Apps
            .Include(a => a.Bank)
            .ThenInclude(b => b.Branch)
            .Where(a => a.Bank.BranchId == branchId)
            .OrderBy(a => a.Name)
            .ToListAsync();
    }

    public async Task<App?> GetByIdAsync(int id)
    {
        return await _context.Apps
            .Include(a => a.Bank)
            .ThenInclude(b => b.Branch)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<App?> GetByIdWithBankAsync(int id)
    {
        return await GetByIdAsync(id); // Same implementation since we always include bank
    }

    public async Task<App> CreateAsync(App app)
    {
        _context.Apps.Add(app);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(app.Id) ?? app;
    }

    public async Task<App> UpdateAsync(App app)
    {
        _context.Apps.Update(app);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(app.Id) ?? app;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var app = await _context.Apps.FindAsync(id);
        if (app == null)
            return false;

        // Check if app has payments
        var hasPayments = await _context.AppPayments.AnyAsync(ap => ap.AppId == id);
        if (hasPayments)
        {
            // Soft delete: just deactivate
            app.Active = false;
            await _context.SaveChangesAsync();
            return true;
        }

        _context.Apps.Remove(app);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(int id)
    {
        return await _context.Apps.AnyAsync(a => a.Id == id);
    }

    public async Task<bool> NameExistsInBankAsync(string name, int bankId, int? excludeId = null)
    {
        return await _context.Apps.AnyAsync(a =>
            a.Name.ToLower() == name.ToLower() &&
            a.BankId == bankId &&
            (!excludeId.HasValue || a.Id != excludeId.Value));
    }

    // Statistics
    public async Task<decimal> GetTotalAppPaymentsAsync(int appId)
    {
        return await _context.AppPayments
            .Where(ap => ap.AppId == appId)
            .SumAsync(ap => ap.Amount);
    }

    public async Task<decimal> GetUnsettledAppPaymentsAsync(int appId)
    {
        return await _context.AppPayments
            .Where(ap => ap.AppId == appId && !ap.IsSetted)
            .SumAsync(ap => ap.Amount);
    }

    public async Task<int> GetTotalAppPaymentsCountAsync(int appId)
    {
        return await _context.AppPayments
            .CountAsync(ap => ap.AppId == appId);
    }

    public async Task<int> GetUnsettledAppPaymentsCountAsync(int appId)
    {
        return await _context.AppPayments
            .CountAsync(ap => ap.AppId == appId && !ap.IsSetted);
    }
}
