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
        string sortOrder = "asc",
        CancellationToken cancellationToken = default);

    Task<IEnumerable<ProductCategory>> GetByBranchIdAsync(int branchId, CancellationToken cancellationToken = default);
    Task<ProductCategory?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ProductCategory?> GetByIdWithProductsAsync(int id, CancellationToken cancellationToken = default);
    Task<ProductCategory> CreateAsync(ProductCategory category, CancellationToken cancellationToken = default);
    Task<ProductCategory> UpdateAsync(ProductCategory category, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> NameExistsInBranchAsync(string name, int branchId, int? excludeId = null, CancellationToken cancellationToken = default);

    // Statistics
    Task<int> GetTotalProductsAsync(int categoryId, CancellationToken cancellationToken = default);
    Task<int> GetActiveProductsAsync(int categoryId, CancellationToken cancellationToken = default);
}
