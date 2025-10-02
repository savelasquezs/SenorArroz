// SenorArroz.Domain/Interfaces/Repositories/IAppRepository.cs
using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IAppRepository
{
    Task<PagedResult<App>> GetPagedAsync(
        int? bankId = null,
        string? name = null,
        bool? active = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "name",
        string sortOrder = "asc");

    Task<IEnumerable<App>> GetByBankIdAsync(int bankId);
    Task<IEnumerable<App>> GetByBranchIdAsync(int branchId);
    Task<App?> GetByIdAsync(int id);
    Task<App?> GetByIdWithBankAsync(int id);
    Task<App> CreateAsync(App app);
    Task<App> UpdateAsync(App app);
    Task<bool> DeleteAsync(int id);
    Task<bool> ExistsAsync(int id);
    Task<bool> NameExistsInBankAsync(string name, int bankId, int? excludeId = null);

    // Statistics
    Task<decimal> GetTotalAppPaymentsAsync(int appId);
    Task<decimal> GetUnsettledAppPaymentsAsync(int appId);
    Task<int> GetTotalAppPaymentsCountAsync(int appId);
    Task<int> GetUnsettledAppPaymentsCountAsync(int appId);
}
