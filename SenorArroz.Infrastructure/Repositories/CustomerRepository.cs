using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Common;
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
        int? branchId,
        string? search = null,
        string? name = null,
        string? phone = null,
        string? whatsAppUsername = null,
        bool? active = null,
        int page = 1,
        int pageSize = 10,
        string sortBy = "name",
        string sortOrder = "asc",
        CancellationToken cancellationToken = default)
    {
        var query = _context.Customers
            .AsNoTracking()
            .Include(c => c.Branch)
            .Include(c => c.Addresses)
            .ThenInclude(a => a.Neighborhood)
            .AsQueryable();

        if (branchId.HasValue)
            query = query.Where(c => c.BranchId == branchId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var usernameTerm = term.TrimStart('@');
            var pattern = $"%{term}%";
            var usernamePattern = $"%{usernameTerm}%";
            query = query.Where(c =>
                EF.Functions.ILike(c.Name, pattern)
                || (c.Phone1 != null && EF.Functions.ILike(c.Phone1, pattern))
                || (c.Phone2 != null && EF.Functions.ILike(c.Phone2, pattern))
                || (c.WhatsAppUsername != null && EF.Functions.ILike(c.WhatsAppUsername, usernamePattern)));
        }

        if (!string.IsNullOrWhiteSpace(name))
            query = query.Where(c => EF.Functions.ILike(c.Name, $"%{name}%"));

        if (!string.IsNullOrWhiteSpace(phone))
            query = query.Where(c => (c.Phone1 != null && c.Phone1.Contains(phone)) ||
                                   (c.Phone2 != null && c.Phone2.Contains(phone)));

        if (!string.IsNullOrWhiteSpace(whatsAppUsername))
        {
            var username = whatsAppUsername.Trim().TrimStart('@');
            query = query.Where(c => c.WhatsAppUsername != null
                && EF.Functions.ILike(c.WhatsAppUsername, $"%{username}%"));
        }

        if (active.HasValue)
            query = query.Where(c => c.Active == active.Value);

        query = sortBy.ToLower() switch
        {
            "name" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(c => c.Name) : query.OrderBy(c => c.Name),
            "phone1" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(c => c.Phone1) : query.OrderBy(c => c.Phone1),
            "createdat" => sortOrder.ToLower() == "desc" ? query.OrderByDescending(c => c.CreatedAt) : query.OrderBy(c => c.CreatedAt),
            _ => query.OrderBy(c => c.Name)
        };

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<Customer?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Customers
            .AsNoTracking()
            .Include(c => c.Branch)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<Customer?> GetByIdWithAddressesAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Customers
            .AsNoTracking()
            .Include(c => c.Branch)
            .Include(c => c.Addresses)
            .ThenInclude(a => a.Neighborhood)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<Customer?> GetByPhoneAsync(string phone, int branchId, CancellationToken cancellationToken = default)
    {
        return await _context.Customers
            .AsNoTracking()
            .Include(c => c.Branch)
            .FirstOrDefaultAsync(c => (c.Phone1 == phone || c.Phone2 == phone) && c.BranchId == branchId && c.Active, cancellationToken);
    }

    public async Task<IEnumerable<Customer>> GetByBranchIdAsync(int branchId, CancellationToken cancellationToken = default)
    {
        return await _context.Customers
            .AsNoTracking()
            .Include(c => c.Branch)
            .Where(c => c.BranchId == branchId && c.Active)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Customer> CreateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdWithAddressesAsync(customer.Id, cancellationToken) ?? customer;
    }

    public async Task<Customer> UpdateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        _context.Customers.Update(customer);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdWithAddressesAsync(customer.Id, cancellationToken) ?? customer;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers.FindAsync([id], cancellationToken);
        if (customer == null)
            return false;

        customer.Active = false;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Customers.AnyAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<bool> PhoneExistsAsync(string phone, int branchId, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Customers
            .Where(c => (c.Phone1 == phone || c.Phone2 == phone) && c.BranchId == branchId && c.Active);

        if (excludeId.HasValue)
            query = query.Where(c => c.Id != excludeId.Value);

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<int> GetTotalOrdersAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .CountAsync(o =>
                o.CustomerId == customerId &&
                o.Status != OrderStatus.Cancelled, cancellationToken);
    }

    public async Task<(DateTime? First, DateTime? Last)> GetOrderDateRangeAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var result = await _context.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == customerId && o.Status != OrderStatus.Cancelled)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                First = (DateTime?)g.Min(o => o.CreatedAt),
                Last  = (DateTime?)g.Max(o => o.CreatedAt)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return (result?.First, result?.Last);
    }

    public async Task<int> GetTotalOrderRevenueAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Where(o =>
                o.CustomerId == customerId &&
                o.Status != OrderStatus.Cancelled)
            .SumAsync(o => o.Total, cancellationToken);
    }
}
