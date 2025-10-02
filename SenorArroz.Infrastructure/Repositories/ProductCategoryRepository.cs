// SenorArroz.Infrastructure/Repositories/ProductCategoryRepository.cs
using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
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
        string sortOrder = "asc")
    {
        var query = _context.ProductCategories
            .Include(pc => pc.Branch)
            .AsQueryable();

        // Branch filter
        if (branchId.HasValue)
        {
            query = query.Where(pc => pc.BranchId == branchId.Value);
        }

        // Name filter
        if (!string.IsNullOrWhiteSpace(name))
        {
            query = query.Where(pc => pc.Name.ToLower().Contains(name.ToLower()));
        }

        // Total count
        var totalCount = await query.CountAsync();

        // Sorting
        query = sortBy.ToLower() switch
        {
            "name" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(pc => pc.Name) : query.OrderBy(pc => pc.Name),
            "branchname" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(pc => pc.Branch.Name) : query.OrderBy(pc => pc.Branch.Name),
            "createdat" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(pc => pc.CreatedAt) : query.OrderBy(pc => pc.CreatedAt),
            _ => query.OrderBy(pc => pc.Name)
        };

        // Pagination
        var categories = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<ProductCategory>
        {
            Items = categories,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<IEnumerable<ProductCategory>> GetByBranchIdAsync(int branchId)
    {
        return await _context.ProductCategories
            .Include(pc => pc.Branch)
            .Where(pc => pc.BranchId == branchId)
            .OrderBy(pc => pc.Name)
            .ToListAsync();
    }

    public async Task<ProductCategory?> GetByIdAsync(int id)
    {
        return await _context.ProductCategories
            .Include(pc => pc.Branch)
            .FirstOrDefaultAsync(pc => pc.Id == id);
    }

    public async Task<ProductCategory?> GetByIdWithProductsAsync(int id)
    {
        return await _context.ProductCategories
            .Include(pc => pc.Branch)
            .Include(pc => pc.Products.Where(p => p.Active))
            .FirstOrDefaultAsync(pc => pc.Id == id);
    }

    public async Task<ProductCategory> CreateAsync(ProductCategory category)
    {
        _context.ProductCategories.Add(category);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(category.Id) ?? category;
    }

    public async Task<ProductCategory> UpdateAsync(ProductCategory category)
    {
        _context.ProductCategories.Update(category);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(category.Id) ?? category;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var category = await _context.ProductCategories.FindAsync(id);
        if (category == null)
            return false;

        // Check if category has products
        var hasProducts = await _context.Products.AnyAsync(p => p.CategoryId == id);
        if (hasProducts)
            return false; // Cannot delete category with products

        _context.ProductCategories.Remove(category);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(int id)
    {
        return await _context.ProductCategories.AnyAsync(pc => pc.Id == id);
    }

    public async Task<bool> NameExistsInBranchAsync(string name, int branchId, int? excludeId = null)
    {
        var query = _context.ProductCategories
            .Where(pc => pc.Name.ToLower() == name.ToLower() && pc.BranchId == branchId);

        if (excludeId.HasValue)
        {
            query = query.Where(pc => pc.Id != excludeId.Value);
        }

        return await query.AnyAsync();
    }

    // Statistics
    public async Task<int> GetTotalProductsAsync(int categoryId)
    {
        return await _context.Products
            .CountAsync(p => p.CategoryId == categoryId);
    }

    public async Task<int> GetActiveProductsAsync(int categoryId)
    {
        return await _context.Products
            .CountAsync(p => p.CategoryId == categoryId && p.Active);
    }
}