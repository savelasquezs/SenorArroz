using SenorArroz.Domain.Entities;
using SenorArroz.Shared.Models;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IExpenseHeaderRepository
{
    Task<PagedResult<ExpenseHeader>> GetPagedAsync(
        int? branchId,
        IReadOnlyCollection<int>? supplierIds,
        int? createdById,
        DateTime? fromDate,
        DateTime? toDate,
        IReadOnlyCollection<string>? bankNames,
        IReadOnlyCollection<string>? categoryNames,
        string? expenseName,
        int page,
        int pageSize,
        string? sortBy,
        string sortOrder,
        CancellationToken cancellationToken = default);

    Task<ExpenseHeader?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ExpenseHeader?> GetByIdWithDetailsAsync(int id, CancellationToken cancellationToken = default);
    Task<ExpenseHeader> CreateAsync(ExpenseHeader expenseHeader, CancellationToken cancellationToken = default);
    Task<ExpenseHeader> UpdateAsync(ExpenseHeader expenseHeader, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
