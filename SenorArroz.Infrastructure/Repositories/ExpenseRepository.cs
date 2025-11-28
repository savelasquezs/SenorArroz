// SenorArroz.Infrastructure/Repositories/ExpenseRepository.cs
using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
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
        string sortOrder = "asc")
    {
        var query = _context.Expenses
            .Include(e => e.Category)
            .AsQueryable();

        // Category filter
        if (categoryId.HasValue)
        {
            query = query.Where(e => e.CategoryId == categoryId.Value);
        }

        // Name filter
        if (!string.IsNullOrWhiteSpace(name))
        {
            query = query.Where(e => e.Name.ToLower().Contains(name.ToLower()));
        }

        // Total count
        var totalCount = await query.CountAsync();

        // Sorting
        query = sortBy.ToLower() switch
        {
            "name" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(e => e.Name) : query.OrderBy(e => e.Name),
            "categoryname" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(e => e.Category.Name) : query.OrderBy(e => e.Category.Name),
            "unit" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(e => e.Unit) : query.OrderBy(e => e.Unit),
            "createdat" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(e => e.CreatedAt) : query.OrderBy(e => e.CreatedAt),
            _ => query.OrderBy(e => e.Name)
        };

        // Pagination
        var expenses = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Expense>
        {
            Items = expenses,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<IEnumerable<Expense>> GetAllAsync()
    {
        return await _context.Expenses
            .Include(e => e.Category)
            .OrderBy(e => e.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<Expense>> GetByCategoryIdAsync(int categoryId)
    {
        return await _context.Expenses
            .Include(e => e.Category)
            .Where(e => e.CategoryId == categoryId)
            .OrderBy(e => e.Name)
            .ToListAsync();
    }

    public async Task<Expense?> GetByIdAsync(int id)
    {
        return await _context.Expenses
            .Include(e => e.Category)
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<Expense?> GetByIdWithCategoryAsync(int id)
    {
        return await GetByIdAsync(id);
    }

    public async Task<Expense> CreateAsync(Expense expense)
    {
        _context.Expenses.Add(expense);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(expense.Id) ?? expense;
    }

    public async Task<Expense> UpdateAsync(Expense expense)
    {
        _context.Expenses.Update(expense);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(expense.Id) ?? expense;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var expense = await _context.Expenses.FindAsync(id);
        if (expense == null)
            return false;

        // Check if expense is used in expense details
        var isUsed = await _context.ExpenseDetails.AnyAsync(ed => ed.ExpenseId == id);
        if (isUsed)
            return false; // Cannot delete expense that is used

        _context.Expenses.Remove(expense);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(int id)
    {
        return await _context.Expenses.AnyAsync(e => e.Id == id);
    }

    public async Task<bool> NameExistsInCategoryAsync(string name, int categoryId, int? excludeId = null)
    {
        var query = _context.Expenses
            .Where(e => e.Name.ToLower() == name.ToLower() && e.CategoryId == categoryId);

        if (excludeId.HasValue)
        {
            query = query.Where(e => e.Id != excludeId.Value);
        }

        return await query.AnyAsync();
    }
}


