using Microsoft.EntityFrameworkCore;
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
        int? supplierId,
        int? createdById,
        DateTime? fromDate,
        DateTime? toDate,
        int page,
        int pageSize,
        string? sortBy,
        string sortOrder,
        CancellationToken cancellationToken = default)
    {
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

        if (supplierId.HasValue)
            query = query.Where(eh => eh.SupplierId == supplierId.Value);

        if (createdById.HasValue)
            query = query.Where(eh => eh.CreatedById == createdById.Value);

        if (fromDate.HasValue)
            query = query.Where(eh => eh.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(eh => eh.CreatedAt <= toDate.Value);

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
        // AsNoTracking() en la carga no resuelve identidad: varias líneas con el mismo ExpenseId traen instancias distintas de Expense → Update() choca.
        // Supplier puede estar rastreado por FindAsync mientras el header trae otra instancia desde Include.
        foreach (var d in expenseHeader.ExpenseDetails)
            d.Expense = null!;
        expenseHeader.Supplier = null!;
        foreach (var p in expenseHeader.ExpenseBankPayments)
            p.Bank = null!;

        _context.ExpenseHeaders.Update(expenseHeader);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdWithDetailsAsync(expenseHeader.Id, cancellationToken) ?? expenseHeader;
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
