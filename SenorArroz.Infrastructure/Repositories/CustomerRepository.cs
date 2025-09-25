using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Infrastructure.Repositories;

public class CustomerRepository : ICustomerRepository
{
    private readonly ApplicationDbContext _context;

    public CustomerRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<Customer>> GetPagedAsync(
        int branchId,
        string? name = null,
        string? phone = null,
        bool? active = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "name",
        string sortOrder = "asc")
    {
        var query = _context.Customers
            .Include(c => c.Branch)
            .Include(c => c.Addresses)
            .ThenInclude(a => a.Neighborhood)
            .Where(c => c.BranchId == branchId);

        // Filters
        if (!string.IsNullOrWhiteSpace(name))
        {
            query = query.Where(c => c.Name.ToLower().Contains(name.ToLower()));
        }

        if (!string.IsNullOrWhiteSpace(phone))
        {
            query = query.Where(c => c.Phone1.Contains(phone) ||
                                   (c.Phone2 != null && c.Phone2.Contains(phone)));
        }

        if (active.HasValue)
        {
            query = query.Where(c => c.Active == active.Value);
        }

        // Total count
        var totalCount = await query.CountAsync();

        // Sorting
        query = sortBy.ToLower() switch
        {
            "name" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(c => c.Name) : query.OrderBy(c => c.Name),
            "phone1" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(c => c.Phone1) : query.OrderBy(c => c.Phone1),
            "createdat" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(c => c.CreatedAt) : query.OrderBy(c => c.CreatedAt),
            _ => query.OrderBy(c => c.Name)
        };

        // Pagination
        var customers = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<Customer>
        {
            Items = customers,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public async Task<Customer?> GetByIdAsync(int id)
    {
        return await _context.Customers
            .Include(c => c.Branch)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<Customer?> GetByIdWithAddressesAsync(int id)
    {
        return await _context.Customers
            .Include(c => c.Branch)
            .Include(c => c.Addresses)
            .ThenInclude(a => a.Neighborhood)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<Customer?> GetByPhoneAsync(string phone, int branchId)
    {
        return await _context.Customers
            .Include(c => c.Branch)
            .FirstOrDefaultAsync(c => (c.Phone1 == phone || c.Phone2 == phone) && c.BranchId == branchId && c.Active);
    }

    public async Task<IEnumerable<Customer>> GetByBranchIdAsync(int branchId)
    {
        return await _context.Customers
            .Include(c => c.Branch)
            .Where(c => c.BranchId == branchId && c.Active)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<Customer> CreateAsync(Customer customer)
    {
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        return await GetByIdWithAddressesAsync(customer.Id) ?? customer;
    }

    public async Task<Customer> UpdateAsync(Customer customer)
    {
        _context.Customers.Update(customer);
        await _context.SaveChangesAsync();

        return await GetByIdWithAddressesAsync(customer.Id) ?? customer;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var customer = await _context.Customers.FindAsync(id);
        if (customer == null)
            return false;

        // Soft delete
        customer.Active = false;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(int id)
    {
        return await _context.Customers.AnyAsync(c => c.Id == id);
    }

    public async Task<bool> PhoneExistsAsync(string phone, int branchId, int? excludeId = null)
    {
        var query = _context.Customers
            .Where(c => (c.Phone1 == phone || c.Phone2 == phone) && c.BranchId == branchId && c.Active);

        if (excludeId.HasValue)
        {
            query = query.Where(c => c.Id != excludeId.Value);
        }

        return await query.AnyAsync();
    }

    public async Task<int> GetTotalOrdersAsync(int customerId)
    {
        return await _context.Orders
            .CountAsync(o => o.CustomerId == customerId);
    }

    public async Task<DateTime?> GetLastOrderDateAsync(int customerId)
    {
        return await _context.Orders
            .Where(o => o.CustomerId == customerId)
            .MaxAsync(o => (DateTime?)o.CreatedAt);
    }
}