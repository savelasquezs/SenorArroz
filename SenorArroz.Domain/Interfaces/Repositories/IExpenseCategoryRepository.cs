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
        string sortOrder = "asc");

    Task<IEnumerable<ExpenseCategory>> GetAllAsync();
    Task<ExpenseCategory?> GetByIdAsync(int id);
    Task<ExpenseCategory?> GetByIdWithExpensesAsync(int id);
    Task<ExpenseCategory> CreateAsync(ExpenseCategory category);
    Task<ExpenseCategory> UpdateAsync(ExpenseCategory category);
    Task<bool> DeleteAsync(int id);
    Task<bool> ExistsAsync(int id);
    Task<bool> NameExistsAsync(string name, int? excludeId = null);

    // Statistics
    Task<int> GetTotalExpensesAsync(int categoryId);
}


