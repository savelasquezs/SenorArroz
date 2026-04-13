// SenorArroz.Infrastructure/Repositories/ExpenseRepository.cs
using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Common;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Infrastructure.Repositories;

public class ExpenseRepository : IExpenseRepository
{
    private readonly ApplicationDbContext _context;

    public ExpenseRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<Expense>> GetPagedAsync(
        int? categoryId = null,
        string? name = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "name",
        string sortOrder = "asc",
        CancellationToken cancellationToken = default)
    {
        var query = _context.Expenses
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.MenuTargets)
            .AsQueryable();

        if (categoryId.HasValue)
            query = query.Where(e => e.CategoryId == categoryId.Value);

        if (!string.IsNullOrWhiteSpace(name))
            query = query.Where(e => EF.Functions.ILike(e.Name, $"%{name}%"));

        query = sortBy.ToLower() switch
        {
            "name" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(e => e.Name) : query.OrderBy(e => e.Name),
            "categoryname" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(e => e.Category.Name) : query.OrderBy(e => e.Category.Name),
            "unit" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(e => e.Unit) : query.OrderBy(e => e.Unit),
            "createdat" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(e => e.CreatedAt) : query.OrderBy(e => e.CreatedAt),
            _ => query.OrderBy(e => e.Name)
        };

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<IEnumerable<Expense>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Expenses
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.MenuTargets)
            .OrderBy(e => e.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Expense>> GetByCategoryIdAsync(int categoryId, CancellationToken cancellationToken = default)
    {
        return await _context.Expenses
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.MenuTargets)
            .Where(e => e.CategoryId == categoryId)
            .OrderBy(e => e.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Expense?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Expenses
            .AsNoTracking()
            .Include(e => e.Category)
            .Include(e => e.MenuTargets)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<Expense?> GetByIdWithCategoryAsync(int id, CancellationToken cancellationToken = default)
    {
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<Expense> CreateAsync(Expense expense, CancellationToken cancellationToken = default)
    {
        _context.Expenses.Add(expense);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(expense.Id, cancellationToken) ?? expense;
    }

    public async Task<Expense> UpdateAsync(Expense expense, CancellationToken cancellationToken = default)
    {
        _context.Expenses.Update(expense);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(expense.Id, cancellationToken) ?? expense;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var expense = await _context.Expenses.FindAsync([id], cancellationToken);
        if (expense == null)
            return false;

        var isUsed = await _context.ExpenseDetails.AnyAsync(ed => ed.ExpenseId == id, cancellationToken);
        if (isUsed)
            return false;

        _context.Expenses.Remove(expense);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Expenses.AnyAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<bool> NameExistsInCategoryAsync(string name, int categoryId, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Expenses
            .Where(e => e.Name.ToLower() == name.ToLower() && e.CategoryId == categoryId);

        if (excludeId.HasValue)
            query = query.Where(e => e.Id != excludeId.Value);

        return await query.AnyAsync(cancellationToken);
    }
}
