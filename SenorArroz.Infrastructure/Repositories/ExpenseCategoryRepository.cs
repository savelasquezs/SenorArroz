// SenorArroz.Infrastructure/Repositories/ExpenseCategoryRepository.cs
using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
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
        string sortOrder = "asc")
    {
        var query = _context.ExpenseCategories
            .Include(ec => ec.Expenses)
            .AsQueryable();

        // Name filter
        if (!string.IsNullOrWhiteSpace(name))
        {
            query = query.Where(ec => ec.Name.ToLower().Contains(name.ToLower()));
        }

        // Total count
        var totalCount = await query.CountAsync();

        // Sorting
        query = sortBy.ToLower() switch
        {
            "name" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(ec => ec.Name) : query.OrderBy(ec => ec.Name),
            "createdat" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(ec => ec.CreatedAt) : query.OrderBy(ec => ec.CreatedAt),
            _ => query.OrderBy(ec => ec.Name)
        };

        // Pagination
        var categories = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<ExpenseCategory>
        {
            Items = categories,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<IEnumerable<ExpenseCategory>> GetAllAsync()
    {
        return await _context.ExpenseCategories
            .Include(ec => ec.Expenses)
            .OrderBy(ec => ec.Name)
            .ToListAsync();
    }

    public async Task<ExpenseCategory?> GetByIdAsync(int id)
    {
        return await _context.ExpenseCategories
            .Include(ec => ec.Expenses)
            .FirstOrDefaultAsync(ec => ec.Id == id);
    }

    public async Task<ExpenseCategory?> GetByIdWithExpensesAsync(int id)
    {
        return await _context.ExpenseCategories
            .Include(ec => ec.Expenses)
            .FirstOrDefaultAsync(ec => ec.Id == id);
    }

    public async Task<ExpenseCategory> CreateAsync(ExpenseCategory category)
    {
        _context.ExpenseCategories.Add(category);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(category.Id) ?? category;
    }

    public async Task<ExpenseCategory> UpdateAsync(ExpenseCategory category)
    {
        _context.ExpenseCategories.Update(category);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(category.Id) ?? category;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var category = await _context.ExpenseCategories.FindAsync(id);
        if (category == null)
            return false;

        // Check if category has expenses
        var hasExpenses = await _context.Expenses.AnyAsync(e => e.CategoryId == id);
        if (hasExpenses)
            return false; // Cannot delete category with expenses

        _context.ExpenseCategories.Remove(category);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(int id)
    {
        return await _context.ExpenseCategories.AnyAsync(ec => ec.Id == id);
    }

    public async Task<bool> NameExistsAsync(string name, int? excludeId = null)
    {
        var query = _context.ExpenseCategories
            .Where(ec => ec.Name.ToLower() == name.ToLower());

        if (excludeId.HasValue)
        {
            query = query.Where(ec => ec.Id != excludeId.Value);
        }

        return await query.AnyAsync();
    }

    // Statistics
    public async Task<int> GetTotalExpensesAsync(int categoryId)
    {
        return await _context.Expenses
            .CountAsync(e => e.CategoryId == categoryId);
    }
}


