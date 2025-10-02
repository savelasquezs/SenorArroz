// SenorArroz.Infrastructure/Repositories/ProductRepository.cs
using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Infrastructure.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly ApplicationDbContext _context;

    public ProductRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<Product>> GetPagedAsync(
        int? branchId = null,
        string? name = null,
        int? categoryId = null,
        bool? active = null,
        int? minPrice = null,
        int? maxPrice = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "name",
        string sortOrder = "asc")
    {
        var query = _context.Products
            .Include(p => p.Category)
            .ThenInclude(c => c.Branch)
            .AsQueryable();

        // Branch filter (via category)
        if (branchId.HasValue)
        {
            query = query.Where(p => p.Category.BranchId == branchId.Value);
        }

        // Name filter
        if (!string.IsNullOrWhiteSpace(name))
        {
            query = query.Where(p => p.Name.ToLower().Contains(name.ToLower()));
        }

        // Category filter
        if (categoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        // Active filter
        if (active.HasValue)
        {
            query = query.Where(p => p.Active == active.Value);
        }

        // Price range filter
        if (minPrice.HasValue)
        {
            query = query.Where(p => p.Price >= minPrice.Value);
        }

        if (maxPrice.HasValue)
        {
            query = query.Where(p => p.Price <= maxPrice.Value);
        }

        // Total count
        var totalCount = await query.CountAsync();

        // Sorting
        query = sortBy.ToLower() switch
        {
            "name" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name),
            "price" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(p => p.Price) : query.OrderBy(p => p.Price),
            "category" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(p => p.Category.Name) : query.OrderBy(p => p.Category.Name),
            "stock" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(p => p.Stock) : query.OrderBy(p => p.Stock),
            "createdat" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(p => p.CreatedAt) : query.OrderBy(p => p.CreatedAt),
            _ => query.OrderBy(p => p.Name)
        };

        // Pagination
        var products = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Product>
        {
            Items = products,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<IEnumerable<Product>> GetByCategoryIdAsync(int categoryId)
    {
        return await _context.Products
            .Include(p => p.Category)
            .ThenInclude(c => c.Branch)
            .Where(p => p.CategoryId == categoryId)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Product>> GetByBranchIdAsync(int branchId)
    {
        return await _context.Products
            .Include(p => p.Category)
            .ThenInclude(c => c.Branch)
            .Where(p => p.Category.BranchId == branchId)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<Product?> GetByIdAsync(int id)
    {
        return await _context.Products
            .Include(p => p.Category)
            .ThenInclude(c => c.Branch)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Product?> GetByIdWithCategoryAsync(int id)
    {
        return await GetByIdAsync(id); // Same implementation since we always include category
    }

    public async Task<Product> CreateAsync(Product product)
    {
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(product.Id) ?? product;
    }

    public async Task<Product> UpdateAsync(Product product)
    {
        _context.Products.Update(product);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(product.Id) ?? product;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null)
            return false;

        // Check if product is used in orders
        var hasOrders = await _context.OrderDetails.AnyAsync(od => od.ProductId == id);
        if (hasOrders)
        {
            // Soft delete: just deactivate
            product.Active = false;
            await _context.SaveChangesAsync();
            return true;
        }
        _context.Products.Remove(product);
        await _context.SaveChangesAsync();
        return true;
        }
    public async Task<bool> ExistsAsync(int id)
    {
                return await _context.Products.AnyAsync(p => p.Id == id);   
    }
    public async Task<bool> NameExistsInCategoryAsync(string name, int categoryId, int? excludeId = null)
    {
        return await _context.Products.AnyAsync(p =>
            p.Name.ToLower() == name.ToLower() &&
            p.CategoryId == categoryId &&
            (!excludeId.HasValue || p.Id != excludeId.Value));

        }
    // Stock management
    public async Task<bool> AdjustStockAsync(int productId, int quantityChange)
    {
        var product = await _context.Products.FindAsync(productId);
        if (product == null)
            return false;
        product.Stock += quantityChange;
        if (product.Stock < 0)
            product.Stock = 0; // Prevent negative stock
        await _context.SaveChangesAsync();
        return true;
    }
    public async Task<int> GetStockAsync(int productId)
    {
        var product = await _context.Products.FindAsync(productId);
        return product?.Stock ?? 0;
    }
    public async Task<bool> SetStockAsync(int productId, int newStock)
    {
        var product = await _context.Products.FindAsync(productId);
        if (product == null)
            return false;
        product.Stock = newStock < 0 ? 0 : newStock; // Prevent negative stock
        await _context.SaveChangesAsync();
        return true;
    }
    // Statistics
    public async Task<int> GetTotalSalesAsync(int productId)
    {
        return await _context.OrderDetails
            .Where(od => od.ProductId == productId)
            .SumAsync(od => od.Quantity);
    }
    public async Task<decimal> GetTotalRevenueAsync(int productId)
    {
        return await _context.OrderDetails
            .Where(od => od.ProductId == productId)
            .SumAsync(od => od.Quantity * od.UnitPrice);
    }
    public async Task<int> GetTotalOrdersAsync(int productId)
    {
        return await _context.OrderDetails
            .Where(od => od.ProductId == productId)
            .Select(od => od.OrderId)
            .Distinct()
            .CountAsync();
    }
    public async Task<int> GetTotalCustomersAsync(int productId)
    {
        return await _context.Orders
            .Where(o => o.OrderDetails.Any(od => od.ProductId == productId))
            .Select(o => o.CustomerId)
            .Distinct()
            .CountAsync();
    }
    public async Task<DateTime?> GetLastSoldAtAsync(int productId)
    {
        return await _context.OrderDetails
            .Where(od => od.ProductId == productId)
            .OrderByDescending(od => od.Order.CreatedAt)
            .Select(od => (DateTime?)od.Order.CreatedAt)
            .FirstOrDefaultAsync();
    }
    }