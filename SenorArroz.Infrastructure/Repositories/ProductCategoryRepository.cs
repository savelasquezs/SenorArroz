// SenorArroz.Infrastructure/Repositories/ProductCategoryRepository.cs
using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Common;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Infrastructure.Repositories;

public class ProductCategoryRepository : IProductCategoryRepository
{
    private readonly ApplicationDbContext _context;

    public ProductCategoryRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<ProductCategory>> GetPagedAsync(
        int? branchId = null,
        string? name = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "name",
        string sortOrder = "asc",
        CancellationToken cancellationToken = default)
    {
        var query = _context.ProductCategories
            .AsNoTracking()
            .Include(pc => pc.Branch)
            .AsQueryable();

        if (branchId.HasValue)
            query = query.Where(pc => pc.BranchId == branchId.Value);

        if (!string.IsNullOrWhiteSpace(name))
            query = query.Where(pc => EF.Functions.ILike(pc.Name, $"%{name}%"));

        query = sortBy.ToLower() switch
        {
            "name" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(pc => pc.Name) : query.OrderBy(pc => pc.Name),
            "branchname" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(pc => pc.Branch.Name) : query.OrderBy(pc => pc.Branch.Name),
            "createdat" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(pc => pc.CreatedAt) : query.OrderBy(pc => pc.CreatedAt),
            _ => query.OrderBy(pc => pc.Name)
        };

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<IEnumerable<ProductCategory>> GetByBranchIdAsync(int branchId, CancellationToken cancellationToken = default)
    {
        return await _context.ProductCategories
            .AsNoTracking()
            .Include(pc => pc.Branch)
            .Where(pc => pc.BranchId == branchId)
            .OrderBy(pc => pc.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<ProductCategory?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.ProductCategories
            .AsNoTracking()
            .Include(pc => pc.Branch)
            .FirstOrDefaultAsync(pc => pc.Id == id, cancellationToken);
    }

    public async Task<ProductCategory?> GetByIdWithProductsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.ProductCategories
            .AsNoTracking()
            .Include(pc => pc.Branch)
            .Include(pc => pc.Products.Where(p => p.Active))
            .FirstOrDefaultAsync(pc => pc.Id == id, cancellationToken);
    }

    public async Task<ProductCategory> CreateAsync(ProductCategory category, CancellationToken cancellationToken = default)
    {
        _context.ProductCategories.Add(category);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(category.Id, cancellationToken) ?? category;
    }

    public async Task<ProductCategory> UpdateAsync(ProductCategory category, CancellationToken cancellationToken = default)
    {
        _context.ProductCategories.Update(category);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(category.Id, cancellationToken) ?? category;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var category = await _context.ProductCategories.FindAsync([id], cancellationToken);
        if (category == null)
            return false;

        var hasProducts = await _context.Products.AnyAsync(p => p.CategoryId == id, cancellationToken);
        if (hasProducts)
            return false;

        _context.ProductCategories.Remove(category);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.ProductCategories.AnyAsync(pc => pc.Id == id, cancellationToken);
    }

    public async Task<bool> NameExistsInBranchAsync(string name, int branchId, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.ProductCategories
            .Where(pc => pc.Name.ToLower() == name.ToLower() && pc.BranchId == branchId);

        if (excludeId.HasValue)
            query = query.Where(pc => pc.Id != excludeId.Value);

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<int> GetTotalProductsAsync(int categoryId, CancellationToken cancellationToken = default)
    {
        return await _context.Products.CountAsync(p => p.CategoryId == categoryId, cancellationToken);
    }

    public async Task<int> GetActiveProductsAsync(int categoryId, CancellationToken cancellationToken = default)
    {
        return await _context.Products.CountAsync(p => p.CategoryId == categoryId && p.Active, cancellationToken);
    }
}
