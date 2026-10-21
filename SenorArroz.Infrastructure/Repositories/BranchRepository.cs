// SenorArroz.Infrastructure/Repositories/BranchRepository.cs
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Common;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Infrastructure.Repositories;

public class BranchRepository : IBranchRepository
{
    private readonly ApplicationDbContext _context;
    private readonly IClock _clock;

    public BranchRepository(ApplicationDbContext context, IClock clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<PagedResult<Branch>> GetPagedAsync(
        string? name = null,
        string? address = null,
        string? phone = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "name",
        string sortOrder = "asc",
        CancellationToken cancellationToken = default)
    {
        var query = _context.Branches.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(name))
            query = query.Where(b => EF.Functions.ILike(b.Name, $"%{name}%"));

        if (!string.IsNullOrWhiteSpace(address))
            query = query.Where(b => EF.Functions.ILike(b.Address, $"%{address}%"));

        if (!string.IsNullOrWhiteSpace(phone))
            query = query.Where(b => b.Phone1.Contains(phone) ||
                                   (b.Phone2 != null && b.Phone2.Contains(phone)));

        query = sortBy.ToLower() switch
        {
            "name" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(b => b.Name) : query.OrderBy(b => b.Name),
            "address" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(b => b.Address) : query.OrderBy(b => b.Address),
            "createdat" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(b => b.CreatedAt) : query.OrderBy(b => b.CreatedAt),
            _ => query.OrderBy(b => b.Name)
        };

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<IEnumerable<Branch>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Branches
            .AsNoTracking()
            .OrderBy(b => b.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Branch?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Branches.FindAsync([id], cancellationToken);
    }

    public async Task<Branch?> GetByIdWithDetailsAsync(int id, CancellationToken cancellationToken = default)
    {
        // Sin Customers: el DTO no los serializa y GetBranchByIdHandler asigna totales vía consultas agregadas.
        // Incluirlos cargaba miles de filas y provocaba timeouts.
        return await _context.Branches
            .AsNoTracking()
            .Include(b => b.Users).ThenInclude(u => u.PayrollExpense)
            .Include(b => b.Neighborhoods)
            .Include(b => b.PrintSettings)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }

    public async Task<Branch?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _context.Branches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Name.ToLower() == name.ToLower(), cancellationToken);
    }

    public async Task<Branch> CreateAsync(Branch branch, CancellationToken cancellationToken = default)
    {
        _context.Branches.Add(branch);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(branch.Id, cancellationToken) ?? branch;
    }

    public async Task<Branch> UpdateAsync(Branch branch, CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.SpecifyKind(_clock.UtcNow, DateTimeKind.Utc);
        branch.UpdatedAt = utcNow;
        _context.Branches.Update(branch);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(branch.Id, cancellationToken) ?? branch;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var branch = await _context.Branches.FindAsync([id], cancellationToken);
        if (branch == null)
            return false;

        var hasUsers = await _context.Users.AnyAsync(u => u.BranchId == id, cancellationToken);
        if (hasUsers)
            return false;

        var hasCustomers = await _context.Customers.AnyAsync(c => c.BranchId == id, cancellationToken);
        if (hasCustomers)
            return false;

        var hasOrders = await _context.Orders.AnyAsync(o => o.BranchId == id, cancellationToken);
        if (hasOrders)
            return false;

        _context.Branches.Remove(branch);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Branches.AnyAsync(b => b.Id == id, cancellationToken);
    }

    public async Task<bool> NameExistsAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Branches.Where(b => b.Name.ToLower() == name.ToLower());

        if (excludeId.HasValue)
            query = query.Where(b => b.Id != excludeId.Value);

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<bool> PhoneExistsAsync(string phone, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Branches.Where(b => b.Phone1 == phone || b.Phone2 == phone);

        if (excludeId.HasValue)
            query = query.Where(b => b.Id != excludeId.Value);

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, BranchListStatistics>> GetListStatisticsForBranchesAsync(
        IReadOnlyCollection<int> branchIds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ids = branchIds.Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<int, BranchListStatistics>();

        // Tres agregaciones en lugar de cinco CountAsync por sucursal. Se mantienen
        // los filtros globales de tenant de EF y RLS PostgreSQL existentes.
        var users = await _context.Users.AsNoTracking()
            .Where(u => ids.Contains(u.BranchId))
            .GroupBy(u => u.BranchId)
            .Select(g => new { BranchId = g.Key, Total = g.Count(), Active = g.Count(u => u.Active) })
            .ToDictionaryAsync(x => x.BranchId, cancellationToken);

        var customers = await _context.Customers.AsNoTracking()
            .Where(c => ids.Contains(c.BranchId))
            .GroupBy(c => c.BranchId)
            .Select(g => new { BranchId = g.Key, Total = g.Count(), Active = g.Count(c => c.Active) })
            .ToDictionaryAsync(x => x.BranchId, cancellationToken);

        var neighborhoods = await _context.Neighborhoods.AsNoTracking()
            .Where(n => ids.Contains(n.BranchId))
            .GroupBy(n => n.BranchId)
            .Select(g => new { BranchId = g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.BranchId, x => x.Total, cancellationToken);

        var result = new Dictionary<int, BranchListStatistics>(ids.Length);
        foreach (var id in ids)
        {
            users.TryGetValue(id, out var userCount);
            customers.TryGetValue(id, out var customerCount);
            result[id] = new BranchListStatistics(
                userCount?.Total ?? 0, userCount?.Active ?? 0,
                customerCount?.Total ?? 0, customerCount?.Active ?? 0,
                neighborhoods.GetValueOrDefault(id));
        }
        return result;
    }

    public async Task<int> GetTotalUsersAsync(int branchId, CancellationToken cancellationToken = default)
    {
        return await _context.Users.CountAsync(u => u.BranchId == branchId, cancellationToken);
    }

    public async Task<int> GetActiveUsersAsync(int branchId, CancellationToken cancellationToken = default)
    {
        return await _context.Users.CountAsync(u => u.BranchId == branchId && u.Active, cancellationToken);
    }

    public async Task<int> GetTotalCustomersAsync(int branchId, CancellationToken cancellationToken = default)
    {
        return await _context.Customers.CountAsync(c => c.BranchId == branchId, cancellationToken);
    }

    public async Task<int> GetActiveCustomersAsync(int branchId, CancellationToken cancellationToken = default)
    {
        return await _context.Customers.CountAsync(c => c.BranchId == branchId && c.Active, cancellationToken);
    }

    public async Task<int> GetTotalNeighborhoodsAsync(int branchId, CancellationToken cancellationToken = default)
    {
        return await _context.Neighborhoods.CountAsync(n => n.BranchId == branchId, cancellationToken);
    }

    public async Task<int> GetTotalOrdersAsync(int branchId, CancellationToken cancellationToken = default)
    {
        return await _context.Orders.CountAsync(o => o.BranchId == branchId, cancellationToken);
    }

    public async Task<int> GetOrdersThisMonthAsync(int branchId, CancellationToken cancellationToken = default)
    {
        var n = _clock.UtcNow;
        var startOfMonth = new DateTime(n.Year, n.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return await _context.Orders.CountAsync(o => o.BranchId == branchId && o.CreatedAt >= startOfMonth, cancellationToken);
    }

    public async Task<int> GetCustomersThisMonthAsync(int branchId, CancellationToken cancellationToken = default)
    {
        var n = _clock.UtcNow;
        var startOfMonth = new DateTime(n.Year, n.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return await _context.Customers.CountAsync(c => c.BranchId == branchId && c.CreatedAt >= startOfMonth, cancellationToken);
    }

    public async Task<Dictionary<string, int>> GetUserRoleStatsAsync(int branchId, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .AsNoTracking()
            .Where(u => u.BranchId == branchId && u.Active)
            .GroupBy(u => u.Role)
            .ToDictionaryAsync(g => g.Key.ToString() ?? "Sin rol", g => g.Count(), cancellationToken);
    }

    public async Task<(int min, int max, decimal average)> GetDeliveryFeeStatsAsync(int branchId, CancellationToken cancellationToken = default)
    {
        var fees = await _context.Neighborhoods
            .AsNoTracking()
            .Where(n => n.BranchId == branchId)
            .Select(n => n.DeliveryFee)
            .ToListAsync(cancellationToken);

        if (!fees.Any())
            return (0, 0, 0);

        return (fees.Min(), fees.Max(), (decimal)fees.Average());
    }
}
