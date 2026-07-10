using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Common;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Infrastructure.Repositories;

public class ExpenseHeaderRepository : IExpenseHeaderRepository
{
    private readonly ApplicationDbContext _context;

    public ExpenseHeaderRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<ExpenseHeader>> GetPagedAsync(
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
        CancellationToken cancellationToken = default)
    {
        var normalizedBankNames = NormalizeStringFilters(bankNames);
        var normalizedCategoryNames = NormalizeStringFilters(categoryNames);
        var normalizedExpenseName = string.IsNullOrWhiteSpace(expenseName)
            ? null
            : expenseName.Trim().ToLower();

        var query = _context.ExpenseHeaders
            .AsNoTracking()
            .Include(eh => eh.Branch)
            .Include(eh => eh.Supplier)
            .Include(eh => eh.CreatedBy)
            .Include(eh => eh.Deliveryman)
            .Include(eh => eh.ExpenseDetails)
                .ThenInclude(ed => ed.Expense)
                    .ThenInclude(e => e.Category)
            .Include(eh => eh.ExpenseBankPayments)
                .ThenInclude(ebp => ebp.Bank)
            .AsQueryable();

        if (branchId.HasValue)
            query = query.Where(eh => eh.BranchId == branchId.Value);

        if (supplierIds is { Count: > 0 })
            query = query.Where(eh => supplierIds.Contains(eh.SupplierId));

        if (createdById.HasValue)
            query = query.Where(eh => eh.CreatedById == createdById.Value);

        if (fromDate.HasValue)
            query = query.Where(eh => eh.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(eh => eh.CreatedAt <= toDate.Value);

        if (normalizedBankNames.Count > 0)
        {
            query = query.Where(eh =>
                eh.ExpenseBankPayments.Any(ebp =>
                    normalizedBankNames.Contains(ebp.Bank.Name.ToLower())));
        }

        if (normalizedCategoryNames.Count > 0)
        {
            query = query.Where(eh =>
                eh.ExpenseDetails.Any(ed =>
                    normalizedCategoryNames.Contains(ed.Expense.Category.Name.ToLower())));
        }

        if (!string.IsNullOrWhiteSpace(normalizedExpenseName))
        {
            query = query.Where(eh =>
                eh.ExpenseDetails.Any(ed =>
                    ed.Expense.Name.ToLower().Contains(normalizedExpenseName)));
        }

        query = ApplySorting(query, sortBy, sortOrder);

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<ExpenseHeader?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.ExpenseHeaders
            .AsNoTracking()
            .Include(eh => eh.Branch)
            .Include(eh => eh.Supplier)
            .Include(eh => eh.CreatedBy)
            .Include(eh => eh.Deliveryman)
            .FirstOrDefaultAsync(eh => eh.Id == id, cancellationToken);
    }

    public async Task<ExpenseHeader?> GetByIdWithDetailsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.ExpenseHeaders
            .AsNoTrackingWithIdentityResolution()
            .Include(eh => eh.Branch)
            .Include(eh => eh.Supplier)
            .Include(eh => eh.CreatedBy)
            .Include(eh => eh.Deliveryman)
            .Include(eh => eh.ExpenseDetails)
                .ThenInclude(ed => ed.Expense)
                    .ThenInclude(e => e.Category)
            .Include(eh => eh.ExpenseBankPayments)
                .ThenInclude(ebp => ebp.Bank)
            .FirstOrDefaultAsync(eh => eh.Id == id, cancellationToken);
    }

    public async Task<ExpenseHeader> CreateAsync(ExpenseHeader expenseHeader, CancellationToken cancellationToken = default)
    {
        _context.ExpenseHeaders.Add(expenseHeader);
        await _context.SaveChangesAsync(cancellationToken);
        return expenseHeader;
    }

    public async Task<ExpenseHeader> UpdateAsync(ExpenseHeader expenseHeader, CancellationToken cancellationToken = default)
    {
        IDbContextTransaction? tx = null;
        try
        {
            // Non-relational providers (notably EF InMemory in tests) do not
            // support transactions. Production uses PostgreSQL and retains the
            // atomic update behavior.
            if (_context.Database.IsRelational())
                tx = await _context.Database.BeginTransactionAsync(cancellationToken);

            ExpenseHeaderUpdateGraphForPersistence.DetachReadOnlyNavigations(expenseHeader);

            var keepIds = expenseHeader.ExpenseDetails
                .Where(d => d.Id > 0)
                .Select(d => d.Id)
                .ToHashSet();

            var detailsToDelete = await _context.ExpenseDetails
                .Where(d => d.HeaderId == expenseHeader.Id && !keepIds.Contains(d.Id))
                .ToListAsync(cancellationToken);

            if (detailsToDelete.Count > 0)
                _context.ExpenseDetails.RemoveRange(detailsToDelete);

            _context.ExpenseHeaders.Update(expenseHeader);
            await _context.SaveChangesAsync(cancellationToken);
            if (tx is not null)
                await tx.CommitAsync(cancellationToken);

            return await GetByIdWithDetailsAsync(expenseHeader.Id, cancellationToken) ?? expenseHeader;
        }
        catch
        {
            if (tx is not null)
                await tx.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (tx is not null)
                await tx.DisposeAsync();
        }
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var expenseHeader = await _context.ExpenseHeaders.FindAsync([id], cancellationToken);
        if (expenseHeader == null)
            return false;

        _context.ExpenseHeaders.Remove(expenseHeader);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static List<string> NormalizeStringFilters(IReadOnlyCollection<string>? values)
    {
        if (values is null || values.Count == 0) return new List<string>();

        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim().ToLower())
            .Distinct()
            .ToList();
    }

    private static IQueryable<ExpenseHeader> ApplySorting(IQueryable<ExpenseHeader> query, string? sortBy, string sortOrder)
    {
        var isDescending = sortOrder?.ToLower() == "desc";

        query = sortBy?.ToLower() switch
        {
            "id" => isDescending ? query.OrderByDescending(eh => eh.Id) : query.OrderBy(eh => eh.Id),
            "total" => isDescending ? query.OrderByDescending(eh => eh.Total) : query.OrderBy(eh => eh.Total),
            "createdat" => isDescending ? query.OrderByDescending(eh => eh.CreatedAt) : query.OrderBy(eh => eh.CreatedAt),
            "updatedat" => isDescending ? query.OrderByDescending(eh => eh.UpdatedAt) : query.OrderBy(eh => eh.UpdatedAt),
            "supplier" => isDescending ? query.OrderByDescending(eh => eh.Supplier.Name) : query.OrderBy(eh => eh.Supplier.Name),
            _ => query.OrderByDescending(eh => eh.CreatedAt)
        };

        return query;
    }
}
