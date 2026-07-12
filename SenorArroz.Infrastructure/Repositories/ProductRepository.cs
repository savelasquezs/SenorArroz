// SenorArroz.Infrastructure/Repositories/ProductRepository.cs
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Models;
using SenorArroz.Infrastructure.Common;
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
        string sortOrder = "asc",
        CancellationToken cancellationToken = default)
    {
        var query = _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .ThenInclude(c => c.Branch)
            .Include(p => p.CommercialProfile)
            .AsQueryable();

        if (branchId.HasValue)
            query = query.Where(p => p.Category.BranchId == branchId.Value);

        if (!string.IsNullOrWhiteSpace(name))
        {
            var term = $"%{name.Trim()}%";
            var people = int.TryParse(new string(name.Where(char.IsDigit).ToArray()), out var parsedPeople) ? parsedPeople : (int?)null;
            query = query.Where(p => EF.Functions.ILike(p.Name, term)
                || (p.CommercialProfile != null && EF.Functions.ILike(p.CommercialProfile.Name, term))
                || (p.CommercialProfile != null && p.CommercialProfile.Description != null && EF.Functions.ILike(p.CommercialProfile.Description, term))
                || (p.CommercialProfile != null && p.CommercialProfile.Ingredients != null && EF.Functions.ILike(p.CommercialProfile.Ingredients, term))
                || (people.HasValue && p.ServesPeopleMin <= people && p.ServesPeopleMax >= people));
        }

        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId.Value);

        if (active.HasValue)
            query = query.Where(p => p.Active == active.Value);

        if (minPrice.HasValue)
            query = query.Where(p => p.Price >= minPrice.Value);

        if (maxPrice.HasValue)
            query = query.Where(p => p.Price <= maxPrice.Value);

        query = sortBy.ToLower() switch
        {
            "name" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name),
            "price" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(p => p.Price) : query.OrderBy(p => p.Price),
            "category" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(p => p.Category.Name) : query.OrderBy(p => p.Category.Name),
            "stock" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(p => p.Stock) : query.OrderBy(p => p.Stock),
            "createdat" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(p => p.CreatedAt) : query.OrderBy(p => p.CreatedAt),
            _ => query.OrderBy(p => p.Name)
        };

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<IEnumerable<Product>> GetByCategoryIdAsync(int categoryId, CancellationToken cancellationToken = default)
    {
        return await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .ThenInclude(c => c.Branch)
            .Include(p => p.CommercialProfile)
            .Where(p => p.CategoryId == categoryId)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Product>> GetByBranchIdAsync(int branchId, CancellationToken cancellationToken = default)
    {
        return await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .ThenInclude(c => c.Branch)
            .Include(p => p.CommercialProfile)
            .Where(p => p.Category.BranchId == branchId)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .ThenInclude(c => c.Branch)
            .Include(p => p.CommercialProfile)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<Product?> GetByIdWithCategoryAsync(int id, CancellationToken cancellationToken = default)
    {
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<Product?> GetByIdWithStatisticsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<Product> CreateAsync(Product product, CancellationToken cancellationToken = default)
    {
        _context.Products.Add(product);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(product.Id, cancellationToken) ?? product;
    }

    public async Task<Product> UpdateAsync(Product product, CancellationToken cancellationToken = default)
    {
        _context.Products.Update(product);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(product.Id, cancellationToken) ?? product;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await _context.Products.FindAsync([id], cancellationToken);
        if (product == null)
            return false;

        var hasOrders = await _context.OrderDetails.AnyAsync(od => od.ProductId == id, cancellationToken);
        if (hasOrders)
        {
            product.Active = false;
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }

        _context.Products.Remove(product);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Products.AnyAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<bool> NameExistsInCategoryAsync(string name, int categoryId, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        return await _context.Products.AnyAsync(p =>
            p.Name.ToLower() == name.ToLower() &&
            p.CategoryId == categoryId &&
            (!excludeId.HasValue || p.Id != excludeId.Value), cancellationToken);
    }
    public Task<bool> CommercialProfileBelongsToBranchAsync(int profileId, int branchId, CancellationToken cancellationToken = default) =>
        _context.CommercialProfiles.AnyAsync(x => x.Id == profileId && x.BranchId == branchId, cancellationToken);

    public async Task<bool> AdjustStockAsync(int productId, int quantityChange, CancellationToken cancellationToken = default)
    {
        var product = await _context.Products.FindAsync([productId], cancellationToken);
        if (product == null)
            return false;
        product.Stock += quantityChange;
        if (product.Stock < 0)
            product.Stock = 0;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> GetStockAsync(int productId, CancellationToken cancellationToken = default)
    {
        var product = await _context.Products.FindAsync([productId], cancellationToken);
        return product?.Stock ?? 0;
    }

    public async Task<bool> SetStockAsync(int productId, int newStock, CancellationToken cancellationToken = default)
    {
        var product = await _context.Products.FindAsync([productId], cancellationToken);
        if (product == null)
            return false;
        product.Stock = newStock < 0 ? 0 : newStock;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> GetTotalSalesAsync(int productId, CancellationToken cancellationToken = default)
    {
        return await _context.OrderDetails
            .Where(od => od.ProductId == productId)
            .SumAsync(od => od.Quantity, cancellationToken);
    }

    public async Task<decimal> GetTotalRevenueAsync(int productId, CancellationToken cancellationToken = default)
    {
        return await _context.OrderDetails
            .Where(od => od.ProductId == productId)
            .SumAsync(od => od.Quantity * od.UnitPrice, cancellationToken);
    }

    public async Task<int> GetTotalOrdersAsync(int productId, CancellationToken cancellationToken = default)
    {
        return await _context.OrderDetails
            .Where(od => od.ProductId == productId)
            .Select(od => od.OrderId)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    public async Task<int> GetTotalCustomersAsync(int productId, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Where(o => o.OrderDetails.Any(od => od.ProductId == productId))
            .Select(o => o.CustomerId)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    public async Task<DateTime?> GetLastSoldAtAsync(int productId, CancellationToken cancellationToken = default)
    {
        return await _context.OrderDetails
            .AsNoTracking()
            .Where(od => od.ProductId == productId)
            .OrderByDescending(od => od.Order.CreatedAt)
            .Select(od => (DateTime?)od.Order.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductSalesUnitsEvolutionPoint>> GetSalesUnitsEvolutionByProductAsync(
        int productId,
        DateTime rangeEndColombiaCalendar,
        int numberOfDays,
        CancellationToken cancellationToken = default)
    {
        if (numberOfDays < 1)
            numberOfDays = 1;

        var end = rangeEndColombiaCalendar.Date;
        var start = end.AddDays(-(numberOfDays - 1));

        var (wideFrom, _) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(start.AddDays(-1), start.AddDays(-1));
        var (_, wideTo) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(end.AddDays(1), end.AddDays(1));

        var rows = await _context.OrderDetails
            .AsNoTracking()
            .Where(od =>
                od.ProductId == productId &&
                od.Order.Status != OrderStatus.Cancelled)
            .Where(od =>
                (od.Order.PrepareAt ?? od.Order.CreatedAt) >= wideFrom &&
                (od.Order.PrepareAt ?? od.Order.CreatedAt) <= wideTo)
            .Select(od => new
            {
                od.Order.CreatedAt,
                od.Order.PrepareAt,
                od.Quantity
            })
            .ToListAsync(cancellationToken);

        var map = new Dictionary<DateTime, int>();
        foreach (var row in rows)
        {
            var bucket = ColombiaTimeHelper.OrderSalesEffectiveColombiaCalendarDate(row.CreatedAt, row.PrepareAt);
            if (bucket < start || bucket > end)
                continue;
            map[bucket] = map.GetValueOrDefault(bucket, 0) + row.Quantity;
        }

        var list = new List<ProductSalesUnitsEvolutionPoint>(numberOfDays);
        for (var d = start; d <= end; d = d.AddDays(1))
            list.Add(new ProductSalesUnitsEvolutionPoint(d, map.GetValueOrDefault(d, 0)));

        return list;
    }
}
