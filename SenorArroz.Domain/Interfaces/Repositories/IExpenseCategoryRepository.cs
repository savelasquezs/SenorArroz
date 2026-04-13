// SenorArroz.Domain/Interfaces/Repositories/IExpenseCategoryRepository.cs
using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IExpenseCategoryRepository
{
    Task<PagedResult<ExpenseCategory>> GetPagedAsync(
        string? name = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "name",
        string sortOrder = "asc",
        CancellationToken cancellationToken = default);

    Task<IEnumerable<ExpenseCategory>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ExpenseCategory?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ExpenseCategory?> GetByIdWithExpensesAsync(int id, CancellationToken cancellationToken = default);
    Task<ExpenseCategory> CreateAsync(ExpenseCategory category, CancellationToken cancellationToken = default);
    Task<ExpenseCategory> UpdateAsync(ExpenseCategory category, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default);

    // Statistics
    Task<int> GetTotalExpensesAsync(int categoryId, CancellationToken cancellationToken = default);
}
