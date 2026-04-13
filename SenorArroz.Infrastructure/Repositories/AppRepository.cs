// SenorArroz.Infrastructure/Repositories/AppRepository.cs
using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Common;
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
        string sortOrder = "asc",
        CancellationToken cancellationToken = default)
    {
        var query = _context.Apps
            .AsNoTracking()
            .Include(a => a.Bank)
            .ThenInclude(b => b.Branch)
            .AsQueryable();

        if (bankId.HasValue)
            query = query.Where(a => a.BankId == bankId.Value);

        if (!string.IsNullOrWhiteSpace(name))
            query = query.Where(a => EF.Functions.ILike(a.Name, $"%{name}%"));

        if (active.HasValue)
            query = query.Where(a => a.Active == active.Value);

        query = sortBy.ToLower() switch
        {
            "name" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(a => a.Name) : query.OrderBy(a => a.Name),
            "bank" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(a => a.Bank.Name) : query.OrderBy(a => a.Bank.Name),
            "createdat" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(a => a.CreatedAt) : query.OrderBy(a => a.CreatedAt),
            _ => query.OrderBy(a => a.Name)
        };

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<IEnumerable<App>> GetByBankIdAsync(int bankId, CancellationToken cancellationToken = default)
    {
        return await _context.Apps
            .AsNoTracking()
            .Include(a => a.Bank)
            .ThenInclude(b => b.Branch)
            .Where(a => a.BankId == bankId)
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<App>> GetByBranchIdAsync(int branchId, CancellationToken cancellationToken = default)
    {
        return await _context.Apps
            .AsNoTracking()
            .Include(a => a.Bank)
            .ThenInclude(b => b.Branch)
            .Where(a => a.Bank.BranchId == branchId)
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<App?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Apps
            .AsNoTracking()
            .Include(a => a.Bank)
            .ThenInclude(b => b.Branch)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<App?> GetByIdWithBankAsync(int id, CancellationToken cancellationToken = default)
    {
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<App> CreateAsync(App app, CancellationToken cancellationToken = default)
    {
        _context.Apps.Add(app);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(app.Id, cancellationToken) ?? app;
    }

    public async Task<App> UpdateAsync(App app, CancellationToken cancellationToken = default)
    {
        _context.Apps.Update(app);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(app.Id, cancellationToken) ?? app;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var app = await _context.Apps.FindAsync([id], cancellationToken);
        if (app == null)
            return false;

        var hasPayments = await _context.AppPayments.AnyAsync(ap => ap.AppId == id, cancellationToken);
        if (hasPayments)
        {
            app.Active = false;
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }

        _context.Apps.Remove(app);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Apps.AnyAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<bool> NameExistsInBankAsync(string name, int bankId, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        return await _context.Apps.AnyAsync(a =>
            a.Name.ToLower() == name.ToLower() &&
            a.BankId == bankId &&
            (!excludeId.HasValue || a.Id != excludeId.Value), cancellationToken);
    }

    public async Task<decimal> GetTotalAppPaymentsAsync(int appId, CancellationToken cancellationToken = default)
    {
        return await _context.AppPayments
            .Where(ap => ap.AppId == appId)
            .SumAsync(ap => ap.Amount, cancellationToken);
    }

    public async Task<decimal> GetUnsettledAppPaymentsAsync(int appId, CancellationToken cancellationToken = default)
    {
        return await _context.AppPayments
            .Where(ap => ap.AppId == appId && !ap.IsSetted)
            .SumAsync(ap => ap.Amount, cancellationToken);
    }

    public async Task<int> GetTotalAppPaymentsCountAsync(int appId, CancellationToken cancellationToken = default)
    {
        return await _context.AppPayments
            .CountAsync(ap => ap.AppId == appId, cancellationToken);
    }

    public async Task<int> GetUnsettledAppPaymentsCountAsync(int appId, CancellationToken cancellationToken = default)
    {
        return await _context.AppPayments
            .CountAsync(ap => ap.AppId == appId && !ap.IsSetted, cancellationToken);
    }
}
