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
        string sortOrder);

    Task<List<Supplier>> GetByBranchAsync(int branchId);
    Task<Supplier?> GetByIdAsync(int id);
    Task<Supplier> CreateAsync(Supplier supplier);
    Task<Supplier> UpdateAsync(Supplier supplier);
    Task<bool> DeleteAsync(int id);
    Task<bool> ExistsAsync(int id);
    Task<bool> NameExistsAsync(string name, int branchId, int? excludeId = null);
    Task<bool> PhoneExistsAsync(string phone, int branchId, int? excludeId = null);
}


