using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Repositories;

public class AddressRepository : IAddressRepository
{
    private readonly ApplicationDbContext _context;

    public AddressRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Address>> GetByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return await _context.Addresses
            .AsNoTracking()
            .Include(a => a.Neighborhood)
            .Include(a => a.Customer)
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Address?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Addresses
            .AsNoTracking()
            .Include(a => a.Neighborhood)
            .Include(a => a.Customer)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<Address?> GetPrimaryByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return await _context.Addresses
            .AsNoTracking()
            .Include(a => a.Neighborhood)
            .Include(a => a.Customer)
            .Where(a => a.CustomerId == customerId && a.IsPrimary)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Address> CreateAsync(Address address, CancellationToken cancellationToken = default)
    {
        if (address.DeliveryFee == 0 && address.NeighborhoodId.HasValue)
        {
            var neighborhood = await _context.Neighborhoods.FindAsync([address.NeighborhoodId.Value], cancellationToken);
            if (neighborhood != null)
            {
                address.DeliveryFee = neighborhood.DeliveryFee;
            }
        }

        _context.Addresses.Add(address);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(address.Id, cancellationToken) ?? address;
    }

    public async Task<Address> UpdateAsync(Address address, CancellationToken cancellationToken = default)
    {
        _context.Addresses.Update(address);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(address.Id, cancellationToken) ?? address;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var address = await _context.Addresses.FindAsync([id], cancellationToken);
        if (address == null)
            return false;

        var hasOrders = await _context.Orders.AnyAsync(o => o.AddressId == id, cancellationToken);
        if (hasOrders)
            return false;

        _context.Addresses.Remove(address);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Addresses.AnyAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<bool> SetPrimaryAddressAsync(int customerId, int addressId, CancellationToken cancellationToken = default)
    {
        await UnsetPrimaryAddressesAsync(customerId, cancellationToken);

        var address = await _context.Addresses
            .FirstOrDefaultAsync(a => a.Id == addressId && a.CustomerId == customerId, cancellationToken);

        if (address == null)
            return false;

        address.IsPrimary = true;
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> UnsetPrimaryAddressesAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var addresses = await _context.Addresses
            .Where(a => a.CustomerId == customerId && a.IsPrimary)
            .ToListAsync(cancellationToken);

        if (!addresses.Any())
            return true;

        foreach (var address in addresses)
        {
            address.IsPrimary = false;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
