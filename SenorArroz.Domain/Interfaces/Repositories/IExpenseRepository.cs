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
        string sortOrder = "asc");

    Task<IEnumerable<Expense>> GetAllAsync();
    Task<IEnumerable<Expense>> GetByCategoryIdAsync(int categoryId);
    Task<Expense?> GetByIdAsync(int id);
    Task<Expense?> GetByIdWithCategoryAsync(int id);
    Task<Expense> CreateAsync(Expense expense);
    Task<Expense> UpdateAsync(Expense expense);
    Task<bool> DeleteAsync(int id);
    Task<bool> ExistsAsync(int id);
    Task<bool> NameExistsInCategoryAsync(string name, int categoryId, int? excludeId = null);
}

