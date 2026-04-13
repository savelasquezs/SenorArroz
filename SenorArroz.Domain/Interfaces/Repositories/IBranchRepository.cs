using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IBranchRepository
{
    Task<PagedResult<Branch>> GetPagedAsync(
        string? name = null,
        string? address = null,
        string? phone = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "name",
        string sortOrder = "asc",
        CancellationToken cancellationToken = default);

    Task<IEnumerable<Branch>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Branch?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Branch?> GetByIdWithDetailsAsync(int id, CancellationToken cancellationToken = default);
    Task<Branch?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<Branch> CreateAsync(Branch branch, CancellationToken cancellationToken = default);
    Task<Branch> UpdateAsync(Branch branch, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default);
    Task<bool> PhoneExistsAsync(string phone, int? excludeId = null, CancellationToken cancellationToken = default);

    // Statistics methods
    Task<int> GetTotalUsersAsync(int branchId, CancellationToken cancellationToken = default);
    Task<int> GetActiveUsersAsync(int branchId, CancellationToken cancellationToken = default);
    Task<int> GetTotalCustomersAsync(int branchId, CancellationToken cancellationToken = default);
    Task<int> GetActiveCustomersAsync(int branchId, CancellationToken cancellationToken = default);
    Task<int> GetTotalNeighborhoodsAsync(int branchId, CancellationToken cancellationToken = default);
    Task<int> GetTotalOrdersAsync(int branchId, CancellationToken cancellationToken = default);
    Task<int> GetOrdersThisMonthAsync(int branchId, CancellationToken cancellationToken = default);
    Task<int> GetCustomersThisMonthAsync(int branchId, CancellationToken cancellationToken = default);

    // User role statistics
    Task<Dictionary<string, int>> GetUserRoleStatsAsync(int branchId, CancellationToken cancellationToken = default);

    // Delivery fee statistics
    Task<(int min, int max, decimal average)> GetDeliveryFeeStatsAsync(int branchId, CancellationToken cancellationToken = default);
}
