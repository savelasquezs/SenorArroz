// SenorArroz.Infrastructure/Repositories/BranchRepository.cs
using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Infrastructure.Repositories;

public class BranchRepository : IBranchRepository
{
    private readonly ApplicationDbContext _context;

    public BranchRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<Branch>> GetPagedAsync(
        string? name = null,
        string? address = null,
        string? phone = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "name",
        string sortOrder = "asc")
    {
        var query = _context.Branches.AsQueryable();

        // Filters
        if (!string.IsNullOrWhiteSpace(name))
        {
            query = query.Where(b => b.Name.ToLower().Contains(name.ToLower()));
        }

        if (!string.IsNullOrWhiteSpace(address))
        {
            query = query.Where(b => b.Address.ToLower().Contains(address.ToLower()));
        }

        if (!string.IsNullOrWhiteSpace(phone))
        {
            query = query.Where(b => b.Phone1.Contains(phone) ||
                                   (b.Phone2 != null && b.Phone2.Contains(phone)));
        }

        // Total count
        var totalCount = await query.CountAsync();

        // Sorting
        query = sortBy.ToLower() switch
        {
            "name" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(b => b.Name) : query.OrderBy(b => b.Name),
            "address" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(b => b.Address) : query.OrderBy(b => b.Address),
            "createdat" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(b => b.CreatedAt) : query.OrderBy(b => b.CreatedAt),
            _ => query.OrderBy(b => b.Name)
        };

        // Pagination
        var branches = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Branch>
        {
            Items = branches,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<IEnumerable<Branch>> GetAllAsync()
    {
        return await _context.Branches
            .OrderBy(b => b.Name)
            .ToListAsync();
    }

    public async Task<Branch?> GetByIdAsync(int id)
    {
        return await _context.Branches.FindAsync(id);
    }

    public async Task<Branch?> GetByIdWithDetailsAsync(int id)
    {
        return await _context.Branches
            .Include(b => b.Users.Where(u => u.Active))
            .Include(b => b.Neighborhoods)
            .Include(b => b.Customers.Where(c => c.Active))
            .FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<Branch?> GetByNameAsync(string name)
    {
        return await _context.Branches
            .FirstOrDefaultAsync(b => b.Name.ToLower() == name.ToLower());
    }

    public async Task<Branch> CreateAsync(Branch branch)
    {
        _context.Branches.Add(branch);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(branch.Id) ?? branch;
    }

    public async Task<Branch> UpdateAsync(Branch branch)
    {
        _context.Branches.Update(branch);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(branch.Id) ?? branch;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var branch = await _context.Branches.FindAsync(id);
        if (branch == null)
            return false;

        // Check if branch has users
        var hasUsers = await _context.Users.AnyAsync(u => u.BranchId == id);
        if (hasUsers)
            return false; // Cannot delete branch with users

        // Check if branch has customers
        var hasCustomers = await _context.Customers.AnyAsync(c => c.BranchId == id);
        if (hasCustomers)
            return false; // Cannot delete branch with customers

        // Check if branch has orders
        var hasOrders = await _context.Orders.AnyAsync(o => o.BranchId == id);
        if (hasOrders)
            return false; // Cannot delete branch with orders

        _context.Branches.Remove(branch);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(int id)
    {
        return await _context.Branches.AnyAsync(b => b.Id == id);
    }

    public async Task<bool> NameExistsAsync(string name, int? excludeId = null)
    {
        var query = _context.Branches.Where(b => b.Name.ToLower() == name.ToLower());

        if (excludeId.HasValue)
        {
            query = query.Where(b => b.Id != excludeId.Value);
        }

        return await query.AnyAsync();
    }

    public async Task<bool> PhoneExistsAsync(string phone, int? excludeId = null)
    {
        var query = _context.Branches.Where(b => b.Phone1 == phone || b.Phone2 == phone);

        if (excludeId.HasValue)
        {
            query = query.Where(b => b.Id != excludeId.Value);
        }

        return await query.AnyAsync();
    }

    // Statistics methods
    public async Task<int> GetTotalUsersAsync(int branchId)
    {
        return await _context.Users
            .CountAsync(u => u.BranchId == branchId);
    }

    public async Task<int> GetActiveUsersAsync(int branchId)
    {
        return await _context.Users
            .CountAsync(u => u.BranchId == branchId && u.Active);
    }

    public async Task<int> GetTotalCustomersAsync(int branchId)
    {
        return await _context.Customers
            .CountAsync(c => c.BranchId == branchId);
    }

    public async Task<int> GetActiveCustomersAsync(int branchId)
    {
        return await _context.Customers
            .CountAsync(c => c.BranchId == branchId && c.Active);
    }

    public async Task<int> GetTotalNeighborhoodsAsync(int branchId)
    {
        return await _context.Neighborhoods
            .CountAsync(n => n.BranchId == branchId);
    }

    public async Task<int> GetTotalOrdersAsync(int branchId)
    {
        return await _context.Orders
            .CountAsync(o => o.BranchId == branchId);
    }

    public async Task<int> GetOrdersThisMonthAsync(int branchId)
    {
        var startOfMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        return await _context.Orders
            .CountAsync(o => o.BranchId == branchId && o.CreatedAt >= startOfMonth);
    }

    public async Task<int> GetCustomersThisMonthAsync(int branchId)
    {
        var startOfMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        return await _context.Customers
            .CountAsync(c => c.BranchId == branchId && c.CreatedAt >= startOfMonth);
    }

    public async Task<Dictionary<string, int>> GetUserRoleStatsAsync(int branchId)
    {
        return await _context.Users
            .Where(u => u.BranchId == branchId && u.Active)
            .GroupBy(u => u.Role)
            .ToDictionaryAsync(g => g.Key.ToString()??"Sin rol", g => g.Count());
    }

    public async Task<(int min, int max, decimal average)> GetDeliveryFeeStatsAsync(int branchId)
    {
        var fees = await _context.Neighborhoods
            .Where(n => n.BranchId == branchId)
            .Select(n => n.DeliveryFee)
            .ToListAsync();

        if (!fees.Any())
            return (0, 0, 0);

        return (fees.Min(), fees.Max(), (decimal)fees.Average());
    }
}
