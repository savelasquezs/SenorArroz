using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface ISupplierRepository
{
    Task<PagedResult<Supplier>> GetPagedAsync(
        int? branchId,
        string? search,
        int page,
        int pageSize,
        string? sortBy,
        string sortOrder,
        CancellationToken cancellationToken = default);

    Task<List<Supplier>> GetByBranchAsync(int branchId, CancellationToken cancellationToken = default);
    Task<Supplier?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Supplier> CreateAsync(Supplier supplier, CancellationToken cancellationToken = default);
    Task<Supplier> UpdateAsync(Supplier supplier, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string name, int branchId, int? excludeId = null, CancellationToken cancellationToken = default);
    Task<bool> PhoneExistsAsync(string phone, int branchId, int? excludeId = null, CancellationToken cancellationToken = default);
}
