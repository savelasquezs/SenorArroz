// SenorArroz.Domain/Interfaces/Repositories/IProductCategoryRepository.cs
using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IProductCategoryRepository
{
    Task<PagedResult<ProductCategory>> GetPagedAsync(
        int? branchId = null,
        string? name = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "name",
        string sortOrder = "asc");

    Task<IEnumerable<ProductCategory>> GetByBranchIdAsync(int branchId);
    Task<ProductCategory?> GetByIdAsync(int id);
    Task<ProductCategory?> GetByIdWithProductsAsync(int id);
    Task<ProductCategory> CreateAsync(ProductCategory category);
    Task<ProductCategory> UpdateAsync(ProductCategory category);
    Task<bool> DeleteAsync(int id);
    Task<bool> ExistsAsync(int id);
    Task<bool> NameExistsInBranchAsync(string name, int branchId, int? excludeId = null);

    // Statistics
    Task<int> GetTotalProductsAsync(int categoryId);
    Task<int> GetActiveProductsAsync(int categoryId);
}