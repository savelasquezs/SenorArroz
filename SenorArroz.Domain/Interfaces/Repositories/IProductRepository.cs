// SenorArroz.Domain/Interfaces/Repositories/IProductRepository.cs
using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IProductRepository
{
    Task<PagedResult<Product>> GetPagedAsync(
        int? branchId = null,
        string? name = null,
        int? categoryId = null,
        bool? active = null,
        int? minPrice = null,
        int? maxPrice = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "name",
        string sortOrder = "asc");

    Task<IEnumerable<Product>> GetByCategoryIdAsync(int categoryId);
    Task<IEnumerable<Product>> GetByBranchIdAsync(int branchId);
    Task<Product?> GetByIdAsync(int id);
    Task<Product?> GetByIdWithCategoryAsync(int id);
    Task<Product> CreateAsync(Product product);
    Task<Product> UpdateAsync(Product product);
    Task<bool> DeleteAsync(int id);
    Task<bool> ExistsAsync(int id);
    Task<bool> NameExistsInCategoryAsync(string name, int categoryId, int? excludeId = null);

    // Statistics
    Task<bool> AdjustStockAsync(int productId, int quantityChange);
    Task<int> GetStockAsync(int productId);

    Task<bool> SetStockAsync(int productId, int newStock);

    Task<int> GetTotalSalesAsync(int productId);

    Task<decimal> GetTotalRevenueAsync(int productId);


    Task<int> GetTotalOrdersAsync(int productId);

    Task<int> GetTotalCustomersAsync(int productId);
    Task<DateTime?> GetLastSoldAtAsync(int productId)
}