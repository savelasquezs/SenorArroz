// SenorArroz.Domain/Interfaces/Repositories/IProductRepository.cs
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Models;
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
        string sortOrder = "asc",
        CancellationToken cancellationToken = default);

    Task<IEnumerable<Product>> GetByCategoryIdAsync(int categoryId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Product>> GetByBranchIdAsync(int branchId, CancellationToken cancellationToken = default);
    Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Product?> GetByIdWithCategoryAsync(int id, CancellationToken cancellationToken = default);
    Task<Product?> GetByIdWithStatisticsAsync(int id, CancellationToken cancellationToken = default);
    Task<Product> CreateAsync(Product product, CancellationToken cancellationToken = default);
    Task<Product> UpdateAsync(Product product, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> NameExistsInCategoryAsync(string name, int categoryId, int? excludeId = null, CancellationToken cancellationToken = default);

    // Statistics
    Task<bool> AdjustStockAsync(int productId, int quantityChange, CancellationToken cancellationToken = default);
    Task<int> GetStockAsync(int productId, CancellationToken cancellationToken = default);

    Task<bool> SetStockAsync(int productId, int newStock, CancellationToken cancellationToken = default);

    Task<int> GetTotalSalesAsync(int productId, CancellationToken cancellationToken = default);

    Task<decimal> GetTotalRevenueAsync(int productId, CancellationToken cancellationToken = default);

    Task<int> GetTotalOrdersAsync(int productId, CancellationToken cancellationToken = default);

    Task<int> GetTotalCustomersAsync(int productId, CancellationToken cancellationToken = default);
    Task<DateTime?> GetLastSoldAtAsync(int productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Serie diaria de unidades vendidas (suma de cantidades en líneas) por día calendario.
    /// Día = <c>ReservedFor</c> si existe, si no <c>Order.CreatedAt</c>. Incluye días sin ventas con 0.
    /// </summary>
    Task<IReadOnlyList<ProductSalesUnitsEvolutionPoint>> GetSalesUnitsEvolutionByProductAsync(
        int productId,
        DateTime rangeEndInclusiveUtc,
        int numberOfDays,
        CancellationToken cancellationToken = default);
}