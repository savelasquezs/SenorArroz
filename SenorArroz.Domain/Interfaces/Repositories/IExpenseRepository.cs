// SenorArroz.Domain/Interfaces/Repositories/IExpenseRepository.cs
using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IExpenseRepository
{
    Task<PagedResult<Expense>> GetPagedAsync(
        int? categoryId = null,
        string? name = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "name",
        string sortOrder = "asc",
        CancellationToken cancellationToken = default);

    Task<IEnumerable<Expense>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Expense>> GetByCategoryIdAsync(int categoryId, CancellationToken cancellationToken = default);
    Task<Expense?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Expense?> GetByIdWithCategoryAsync(int id, CancellationToken cancellationToken = default);
    Task<Expense> CreateAsync(Expense expense, CancellationToken cancellationToken = default);
    Task<Expense> UpdateAsync(Expense expense, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> NameExistsInCategoryAsync(string name, int categoryId, int? excludeId = null, CancellationToken cancellationToken = default);
}
