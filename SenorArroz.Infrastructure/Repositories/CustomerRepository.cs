using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Common;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Shared.Models;
using Npgsql;

namespace SenorArroz.Infrastructure.Repositories;

public class CustomerRepository : ICustomerRepository
{
    private readonly ApplicationDbContext _context;
    private readonly int _tenantId;

    public CustomerRepository(ApplicationDbContext context, ICurrentTenant? currentTenant = null)
    {
        _context = context;
        _tenantId = currentTenant?.TenantId ?? context.CurrentTenantIdForFilter;
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
        var mobileIdentity = new[] { phone, search }
            .Select(candidate => ColombianPhoneNormalizer.TryNormalize(candidate, out var normalized) ? normalized : null)
            .FirstOrDefault(normalized => normalized is not null);

        var query = _context.Customers
            .AsNoTracking()
            .Include(c => c.Branch)
            .Include(c => c.Phones)
            .Include(c => c.Addresses)
            .ThenInclude(a => a.Neighborhood)
            .Include(c => c.Addresses)
            .ThenInclude(a => a.BranchServices)
            .ThenInclude(s => s.Neighborhood)
            .AsQueryable();
        query = query.Where(c => c.TenantId == _tenantId);

        // BranchId is origin-branch compatibility data, not customer identity.
        // An exact mobile lookup must therefore work from every authorized branch
        // in the current tenant; ordinary POS lists remain branch-filtered.
        if (branchId.HasValue && mobileIdentity is null)
            query = query.Where(c => c.BranchId == branchId.Value);

        if (mobileIdentity is not null)
        {
            query = query.Where(c => c.Phones.Any(p => p.Active && p.PhoneNormalized == mobileIdentity));
        }
        else if (!string.IsNullOrWhiteSpace(search))
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

        if (mobileIdentity is null && !string.IsNullOrWhiteSpace(phone))
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
            .Include(c => c.Phones)
            .FirstOrDefaultAsync(c => c.TenantId == _tenantId && c.Id == id, cancellationToken);
    }

    public async Task<Customer?> GetByIdWithAddressesAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Customers
            .AsNoTracking()
            .Include(c => c.Branch)
            .Include(c => c.Phones)
            .Include(c => c.Addresses)
            .ThenInclude(a => a.Neighborhood)
            .Include(c => c.Addresses)
            .ThenInclude(a => a.BranchServices)
            .ThenInclude(s => s.Neighborhood)
            .FirstOrDefaultAsync(c => c.TenantId == _tenantId && c.Id == id, cancellationToken);
    }

    public async Task<Customer?> GetByPhoneAsync(string phone, int tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId != _tenantId)
            return null;
        if (!ColombianPhoneNormalizer.TryNormalize(phone, out var normalized))
            return null;
        return await _context.Customers
            .AsNoTracking()
            .Include(c => c.Branch)
            .Include(c => c.Phones)
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Active &&
                (c.Phones.Any(p => p.Active && p.PhoneNormalized == normalized)
                 || c.Phone1 == normalized || c.Phone2 == normalized), cancellationToken);
    }

    public async Task<IEnumerable<Customer>> GetByBranchIdAsync(int branchId, CancellationToken cancellationToken = default)
    {
        return await _context.Customers
            .AsNoTracking()
            .Include(c => c.Branch)
            .Include(c => c.Phones)
            .Where(c => c.TenantId == _tenantId && c.BranchId == branchId && c.Active)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Customer> CreateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        if (customer.TenantId != _tenantId)
            throw new InvalidOperationException("No se puede crear un cliente para otro tenant.");
        foreach (var phone in customer.Phones)
        {
            phone.TenantId = customer.TenantId;
            phone.Customer = customer;
        }
        _context.Customers.Add(customer);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
            && customer.Phones.FirstOrDefault()?.PhoneNormalized is string winningPhone)
        {
            _context.ChangeTracker.Clear();
            var winner = await GetByPhoneAsync(winningPhone, customer.TenantId, cancellationToken);
            if (winner is not null)
                return winner;
            throw;
        }

        return customer;
    }

    public async Task<Customer> UpdateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        if (customer.TenantId != _tenantId)
            throw new InvalidOperationException("No se puede actualizar un cliente de otro tenant.");
        var normalizedPhones = new[] { customer.Phone1, customer.Phone2 }
            .Where(x => ColombianPhoneNormalizer.TryNormalize(x, out _))
            .Select(x => ColombianPhoneNormalizer.Normalize(x))
            .Distinct()
            .ToList();
        var primaryPhone = normalizedPhones.FirstOrDefault();
        var storedPhones = await _context.CustomerPhones.Where(x => x.TenantId == _tenantId && x.CustomerId == customer.Id).ToListAsync(cancellationToken);
        foreach (var stored in storedPhones)
            stored.Active = normalizedPhones.Contains(stored.PhoneNormalized);
        foreach (var normalized in normalizedPhones.Where(x => storedPhones.All(p => p.PhoneNormalized != x)))
            _context.CustomerPhones.Add(new CustomerPhone
            {
                TenantId = customer.TenantId,
                CustomerId = customer.Id,
                PhoneNormalized = normalized,
                IsPrimary = normalized == primaryPhone,
                Active = true
            });
        foreach (var stored in storedPhones)
            stored.IsPrimary = stored.Active && stored.PhoneNormalized == primaryPhone;
        _context.Customers.Update(customer);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdWithAddressesAsync(customer.Id, cancellationToken) ?? customer;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var customer = await _context.Customers.FirstOrDefaultAsync(x => x.TenantId == _tenantId && x.Id == id, cancellationToken);
        if (customer == null)
            return false;

        customer.Active = false;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Customers.AnyAsync(c => c.TenantId == _tenantId && c.Id == id, cancellationToken);
    }

    public async Task<bool> PhoneExistsAsync(string phone, int tenantId, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        if (tenantId != _tenantId)
            return false;
        if (!ColombianPhoneNormalizer.TryNormalize(phone, out var normalized))
            return false;
        var query = _context.Customers
            .Where(c => c.TenantId == tenantId && c.Active &&
                (c.Phones.Any(p => p.Active && p.PhoneNormalized == normalized)
                 || c.Phone1 == normalized || c.Phone2 == normalized));

        if (excludeId.HasValue)
            query = query.Where(c => c.Id != excludeId.Value);

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<int> GetTotalOrdersAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .CountAsync(o =>
                o.TenantId == _tenantId &&
                o.CustomerId == customerId &&
                o.Status != OrderStatus.Cancelled &&
                o.Status != OrderStatus.AwaitingPayment, cancellationToken);
    }

    public async Task<(DateTime? First, DateTime? Last)> GetOrderDateRangeAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var result = await _context.Orders
            .AsNoTracking()
            .Where(o => o.TenantId == _tenantId && o.CustomerId == customerId
                && o.Status != OrderStatus.Cancelled
                && o.Status != OrderStatus.AwaitingPayment)
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
                o.TenantId == _tenantId &&
                o.CustomerId == customerId &&
                o.Status != OrderStatus.Cancelled &&
                o.Status != OrderStatus.AwaitingPayment)
            .SumAsync(o => o.Total, cancellationToken);
    }
}
