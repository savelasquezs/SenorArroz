using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
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
        string sortOrder)
    {
        var query = _context.ExpenseHeaders
            .Include(eh => eh.Branch)
            .Include(eh => eh.Supplier)
            .Include(eh => eh.CreatedBy)
            .Include(eh => eh.ExpenseDetails)
                .ThenInclude(ed => ed.Expense)
                    .ThenInclude(e => e.Category)
            .Include(eh => eh.ExpenseBankPayments)
                .ThenInclude(ebp => ebp.Bank)
            .AsQueryable();

        // Filtrar por sucursal
        if (branchId.HasValue)
        {
            query = query.Where(eh => eh.BranchId == branchId.Value);
        }

        // Filtrar por proveedor
        if (supplierId.HasValue)
        {
            query = query.Where(eh => eh.SupplierId == supplierId.Value);
        }

        // Filtrar por creador (para cashier)
        if (createdById.HasValue)
        {
            query = query.Where(eh => eh.CreatedById == createdById.Value);
        }

        // Filtrar por rango de fechas
        if (fromDate.HasValue)
        {
            query = query.Where(eh => eh.CreatedAt >= fromDate.Value);
        }
        if (toDate.HasValue)
        {
            query = query.Where(eh => eh.CreatedAt <= toDate.Value);
        }

        // Aplicar ordenamiento
        query = ApplySorting(query, sortBy, sortOrder);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<ExpenseHeader>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        };
    }

    public async Task<ExpenseHeader?> GetByIdAsync(int id)
    {
        return await _context.ExpenseHeaders
            .Include(eh => eh.Branch)
            .Include(eh => eh.Supplier)
            .Include(eh => eh.CreatedBy)
            .FirstOrDefaultAsync(eh => eh.Id == id);
    }

    public async Task<ExpenseHeader?> GetByIdWithDetailsAsync(int id)
    {
        return await _context.ExpenseHeaders
            .Include(eh => eh.Branch)
            .Include(eh => eh.Supplier)
            .Include(eh => eh.CreatedBy)
            .Include(eh => eh.ExpenseDetails)
                .ThenInclude(ed => ed.Expense)
                    .ThenInclude(e => e.Category)
            .Include(eh => eh.ExpenseBankPayments)
                .ThenInclude(ebp => ebp.Bank)
            .FirstOrDefaultAsync(eh => eh.Id == id);
    }

    public async Task<ExpenseHeader> CreateAsync(ExpenseHeader expenseHeader)
    {
        _context.ExpenseHeaders.Add(expenseHeader);
        await _context.SaveChangesAsync();
        return expenseHeader;
    }

    public async Task<ExpenseHeader> UpdateAsync(ExpenseHeader expenseHeader)
    {
        _context.ExpenseHeaders.Update(expenseHeader);
        await _context.SaveChangesAsync();
        
        // Recargar con todas las navegaciones
        return await GetByIdWithDetailsAsync(expenseHeader.Id) ?? expenseHeader;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var expenseHeader = await _context.ExpenseHeaders.FindAsync(id);
        if (expenseHeader == null)
        {
            return false;
        }

        _context.ExpenseHeaders.Remove(expenseHeader);
        await _context.SaveChangesAsync();
        return true;
    }

    private IQueryable<ExpenseHeader> ApplySorting(IQueryable<ExpenseHeader> query, string? sortBy, string sortOrder)
    {
        var isDescending = sortOrder?.ToLower() == "desc";

        query = sortBy?.ToLower() switch
        {
            "id" => isDescending ? query.OrderByDescending(eh => eh.Id) : query.OrderBy(eh => eh.Id),
            "total" => isDescending ? query.OrderByDescending(eh => eh.Total) : query.OrderBy(eh => eh.Total),
            "createdat" => isDescending ? query.OrderByDescending(eh => eh.CreatedAt) : query.OrderBy(eh => eh.CreatedAt),
            "updatedat" => isDescending ? query.OrderByDescending(eh => eh.UpdatedAt) : query.OrderBy(eh => eh.UpdatedAt),
            "supplier" => isDescending ? query.OrderByDescending(eh => eh.Supplier.Name) : query.OrderBy(eh => eh.Supplier.Name),
            _ => query.OrderByDescending(eh => eh.CreatedAt) // Por defecto: más recientes primero
        };

        return query;
    }
}


