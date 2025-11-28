using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IExpenseHeaderRepository
{
    Task<PagedResult<ExpenseHeader>> GetPagedAsync(
        int? branchId,
        int? supplierId,
        int? createdById,
        DateTime? fromDate,
        DateTime? toDate,
        int page,
        int pageSize,
        string? sortBy,
        string sortOrder);
    
    Task<ExpenseHeader?> GetByIdAsync(int id);
    Task<ExpenseHeader?> GetByIdWithDetailsAsync(int id);
    Task<ExpenseHeader> CreateAsync(ExpenseHeader expenseHeader);
    Task<ExpenseHeader> UpdateAsync(ExpenseHeader expenseHeader);
    Task<bool> DeleteAsync(int id);
}


