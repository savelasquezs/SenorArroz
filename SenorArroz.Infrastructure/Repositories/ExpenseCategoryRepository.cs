// SenorArroz.Infrastructure/Repositories/ExpenseCategoryRepository.cs
using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Common;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Infrastructure.Repositories;

public class ExpenseCategoryRepository : IExpenseCategoryRepository
{
    private readonly ApplicationDbContext _context;

    public ExpenseCategoryRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<ExpenseCategory>> GetPagedAsync(
        string? name = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "name",
        string sortOrder = "asc",
        CancellationToken cancellationToken = default)
    {
        var query = _context.ExpenseCategories
            .AsNoTracking()
            .Include(ec => ec.Expenses)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(name))
            query = query.Where(ec => EF.Functions.ILike(ec.Name, $"%{name}%"));

        query = sortBy.ToLower() switch
        {
            "name" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(ec => ec.Name) : query.OrderBy(ec => ec.Name),
            "createdat" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(ec => ec.CreatedAt) : query.OrderBy(ec => ec.CreatedAt),
            _ => query.OrderBy(ec => ec.Name)
        };

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<IEnumerable<ExpenseCategory>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ExpenseCategories
            .AsNoTracking()
            .Include(ec => ec.Expenses)
            .OrderBy(ec => ec.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<ExpenseCategory?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.ExpenseCategories
            .AsNoTracking()
            .Include(ec => ec.Expenses)
            .FirstOrDefaultAsync(ec => ec.Id == id, cancellationToken);
    }

    public async Task<ExpenseCategory?> GetByIdWithExpensesAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.ExpenseCategories
            .AsNoTracking()
            .Include(ec => ec.Expenses)
            .FirstOrDefaultAsync(ec => ec.Id == id, cancellationToken);
    }

    public async Task<ExpenseCategory> CreateAsync(ExpenseCategory category, CancellationToken cancellationToken = default)
    {
        _context.ExpenseCategories.Add(category);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(category.Id, cancellationToken) ?? category;
    }

    public async Task<ExpenseCategory> UpdateAsync(ExpenseCategory category, CancellationToken cancellationToken = default)
    {
        _context.ExpenseCategories.Update(category);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(category.Id, cancellationToken) ?? category;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var category = await _context.ExpenseCategories.FindAsync([id], cancellationToken);
        if (category == null)
            return false;

        var hasExpenses = await _context.Expenses.AnyAsync(e => e.CategoryId == id, cancellationToken);
        if (hasExpenses)
            return false;

        _context.ExpenseCategories.Remove(category);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.ExpenseCategories.AnyAsync(ec => ec.Id == id, cancellationToken);
    }

    public async Task<bool> NameExistsAsync(string name, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.ExpenseCategories
            .Where(ec => ec.Name.ToLower() == name.ToLower());

        if (excludeId.HasValue)
            query = query.Where(ec => ec.Id != excludeId.Value);

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<int> GetTotalExpensesAsync(int categoryId, CancellationToken cancellationToken = default)
    {
        return await _context.Expenses.CountAsync(e => e.CategoryId == categoryId, cancellationToken);
    }
}
