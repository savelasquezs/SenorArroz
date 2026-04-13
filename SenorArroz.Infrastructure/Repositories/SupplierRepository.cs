using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Common;
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
        string sortOrder,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Suppliers
            .AsNoTracking()
            .Include(s => s.Branch)
            .AsQueryable();

        if (branchId.HasValue)
            query = query.Where(s => s.BranchId == branchId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(s =>
                EF.Functions.ILike(s.Name, pattern) ||
                s.Phone.Contains(search) ||
                (s.Email != null && EF.Functions.ILike(s.Email, pattern)));
        }

        query = ApplySorting(query, sortBy, sortOrder);

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<List<Supplier>> GetByBranchAsync(int branchId, CancellationToken cancellationToken = default)
    {
        return await _context.Suppliers
            .AsNoTracking()
            .Where(s => s.BranchId == branchId)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Supplier?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Suppliers
            .AsNoTracking()
            .Include(s => s.Branch)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<Supplier> CreateAsync(Supplier supplier, CancellationToken cancellationToken = default)
    {
        _context.Suppliers.Add(supplier);
        await _context.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(supplier.Id, cancellationToken) ?? supplier;
    }

    public async Task<Supplier> UpdateAsync(Supplier supplier, CancellationToken cancellationToken = default)
    {
        _context.Suppliers.Update(supplier);
        await _context.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(supplier.Id, cancellationToken) ?? supplier;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var supplier = await _context.Suppliers.FindAsync([id], cancellationToken);
        if (supplier == null)
            return false;

        _context.Suppliers.Remove(supplier);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
    {
        return _context.Suppliers.AnyAsync(s => s.Id == id, cancellationToken);
    }

    public Task<bool> NameExistsAsync(string name, int branchId, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        var normalized = name.Trim().ToLower();
        var query = _context.Suppliers.Where(s =>
            s.BranchId == branchId &&
            s.Name.ToLower() == normalized);

        if (excludeId.HasValue)
            query = query.Where(s => s.Id != excludeId.Value);

        return query.AnyAsync(cancellationToken);
    }

    public Task<bool> PhoneExistsAsync(string phone, int branchId, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        var normalized = phone.Trim();
        var query = _context.Suppliers.Where(s =>
            s.BranchId == branchId &&
            s.Phone == normalized);

        if (excludeId.HasValue)
            query = query.Where(s => s.Id != excludeId.Value);

        return query.AnyAsync(cancellationToken);
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
