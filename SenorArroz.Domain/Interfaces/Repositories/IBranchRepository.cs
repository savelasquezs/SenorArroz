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
        string sortOrder = "asc");

    Task<IEnumerable<Branch>> GetAllAsync();
    Task<Branch?> GetByIdAsync(int id);
    Task<Branch?> GetByIdWithDetailsAsync(int id);
    Task<Branch?> GetByNameAsync(string name);
    Task<Branch> CreateAsync(Branch branch);
    Task<Branch> UpdateAsync(Branch branch);
    Task<bool> DeleteAsync(int id);
    Task<bool> ExistsAsync(int id);
    Task<bool> NameExistsAsync(string name, int? excludeId = null);
    Task<bool> PhoneExistsAsync(string phone, int? excludeId = null);

    // Statistics methods
    Task<int> GetTotalUsersAsync(int branchId);
    Task<int> GetActiveUsersAsync(int branchId);
    Task<int> GetTotalCustomersAsync(int branchId);
    Task<int> GetActiveCustomersAsync(int branchId);
    Task<int> GetTotalNeighborhoodsAsync(int branchId);
    Task<int> GetTotalOrdersAsync(int branchId);
    Task<int> GetOrdersThisMonthAsync(int branchId);
    Task<int> GetCustomersThisMonthAsync(int branchId);

    // User role statistics
    Task<Dictionary<string, int>> GetUserRoleStatsAsync(int branchId);

    // Delivery fee statistics
    Task<(int min, int max, decimal average)> GetDeliveryFeeStatsAsync(int branchId);
}
