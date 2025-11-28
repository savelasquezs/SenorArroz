using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Infrastructure.Repositories;

public class SupplierRepository : ISupplierRepository
{
    private readonly ApplicationDbContext _context;

    public SupplierRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<Supplier>> GetPagedAsync(
        int? branchId,
        string? search,
        int page,
        int pageSize,
        string? sortBy,
        string sortOrder)
    {
        var query = _context.Suppliers
            .Include(s => s.Branch)
            .AsQueryable();

        if (branchId.HasValue)
        {
            query = query.Where(s => s.BranchId == branchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var text = search.Trim().ToLower();
            query = query.Where(s =>
                s.Name.ToLower().Contains(text) ||
                s.Phone.Contains(search) ||
                (s.Email != null && s.Email.ToLower().Contains(text)));
        }

        var totalCount = await query.CountAsync();

        query = ApplySorting(query, sortBy, sortOrder);

        var suppliers = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Supplier>
        {
            Items = suppliers,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<List<Supplier>> GetByBranchAsync(int branchId)
    {
        return await _context.Suppliers
            .Where(s => s.BranchId == branchId)
            .OrderBy(s => s.Name)
            .ToListAsync();
    }

    public async Task<Supplier?> GetByIdAsync(int id)
    {
        return await _context.Suppliers
            .Include(s => s.Branch)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<Supplier> CreateAsync(Supplier supplier)
    {
        _context.Suppliers.Add(supplier);
        await _context.SaveChangesAsync();
        return await GetByIdAsync(supplier.Id) ?? supplier;
    }

    public async Task<Supplier> UpdateAsync(Supplier supplier)
    {
        _context.Suppliers.Update(supplier);
        await _context.SaveChangesAsync();
        return await GetByIdAsync(supplier.Id) ?? supplier;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var supplier = await _context.Suppliers.FindAsync(id);
        if (supplier == null)
        {
            return false;
        }

        _context.Suppliers.Remove(supplier);
        await _context.SaveChangesAsync();
        return true;
    }

    public Task<bool> ExistsAsync(int id)
    {
        return _context.Suppliers.AnyAsync(s => s.Id == id);
    }

    public Task<bool> NameExistsAsync(string name, int branchId, int? excludeId = null)
    {
        var normalized = name.Trim().ToLower();
        var query = _context.Suppliers.Where(s =>
            s.BranchId == branchId &&
            s.Name.ToLower() == normalized);

        if (excludeId.HasValue)
        {
            query = query.Where(s => s.Id != excludeId.Value);
        }

        return query.AnyAsync();
    }

    public Task<bool> PhoneExistsAsync(string phone, int branchId, int? excludeId = null)
    {
        var normalized = phone.Trim();
        var query = _context.Suppliers.Where(s =>
            s.BranchId == branchId &&
            s.Phone == normalized);

        if (excludeId.HasValue)
        {
            query = query.Where(s => s.Id != excludeId.Value);
        }

        return query.AnyAsync();
    }

    private static IQueryable<Supplier> ApplySorting(
        IQueryable<Supplier> query,
        string? sortBy,
        string sortOrder)
    {
        var descending = string.Equals(sortOrder, "desc", StringComparison.OrdinalIgnoreCase);
        return sortBy?.ToLower() switch
        {
            "phone" => descending ? query.OrderByDescending(s => s.Phone) : query.OrderBy(s => s.Phone),
            "createdat" => descending ? query.OrderByDescending(s => s.CreatedAt) : query.OrderBy(s => s.CreatedAt),
            "updatedat" => descending ? query.OrderByDescending(s => s.UpdatedAt) : query.OrderBy(s => s.UpdatedAt),
            _ => descending ? query.OrderByDescending(s => s.Name) : query.OrderBy(s => s.Name)
        };
    }
}


