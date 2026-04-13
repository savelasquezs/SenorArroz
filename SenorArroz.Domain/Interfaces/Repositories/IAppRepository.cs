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
        string sortOrder = "asc",
        CancellationToken cancellationToken = default);

    Task<IEnumerable<App>> GetByBankIdAsync(int bankId, CancellationToken cancellationToken = default);
    Task<IEnumerable<App>> GetByBranchIdAsync(int branchId, CancellationToken cancellationToken = default);
    Task<App?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<App?> GetByIdWithBankAsync(int id, CancellationToken cancellationToken = default);
    Task<App> CreateAsync(App app, CancellationToken cancellationToken = default);
    Task<App> UpdateAsync(App app, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> NameExistsInBankAsync(string name, int bankId, int? excludeId = null, CancellationToken cancellationToken = default);

    // Statistics
    Task<decimal> GetTotalAppPaymentsAsync(int appId, CancellationToken cancellationToken = default);
    Task<decimal> GetUnsettledAppPaymentsAsync(int appId, CancellationToken cancellationToken = default);
    Task<int> GetTotalAppPaymentsCountAsync(int appId, CancellationToken cancellationToken = default);
    Task<int> GetUnsettledAppPaymentsCountAsync(int appId, CancellationToken cancellationToken = default);
}
