// SenorArroz.Domain/Interfaces/Repositories/IBankRepository.cs
using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IBankRepository
{
    Task<PagedResult<Bank>> GetPagedAsync(
        int? branchId = null,
        string? name = null,
        bool? active = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "name",
        string sortOrder = "asc");

    Task<IEnumerable<Bank>> GetByBranchIdAsync(int branchId);
    Task<Bank?> GetByIdAsync(int id);
    Task<Bank?> GetByIdWithAppsAsync(int id);
    Task<Bank> CreateAsync(Bank bank);
    Task<Bank> UpdateAsync(Bank bank);
    Task<bool> DeleteAsync(int id);
    Task<bool> ExistsAsync(int id);
    Task<bool> NameExistsInBranchAsync(string name, int branchId, int? excludeId = null);

    // Statistics
    Task<int> GetTotalAppsAsync(int bankId);
    Task<int> GetActiveAppsAsync(int bankId);
    Task<decimal> GetTotalBankPaymentsAsync(int bankId);
    Task<decimal> GetTotalExpenseBankPaymentsAsync(int bankId);
    Task<decimal> GetCurrentBalanceAsync(int bankId);
}
