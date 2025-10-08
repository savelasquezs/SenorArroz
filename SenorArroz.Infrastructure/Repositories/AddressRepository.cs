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

    public async Task<IEnumerable<Address>> GetByCustomerIdAsync(int customerId)
    {
        return await _context.Addresses
            .Include(a => a.Neighborhood)
            .Include(a => a.Customer)
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<Address?> GetByIdAsync(int id)
    {
        return await _context.Addresses
            .Include(a => a.Neighborhood)
            .Include(a => a.Customer)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<Address?> GetPrimaryByCustomerIdAsync(int customerId)
    {
        return await _context.Addresses
            .Include(a => a.Neighborhood)
            .Include(a => a.Customer)
            .Where(a => a.CustomerId == customerId && a.IsPrimary)
            .FirstOrDefaultAsync();
    }

    public async Task<Address> CreateAsync(Address address)
    {
        // Get delivery fee from neighborhood if not set
        if (address.DeliveryFee == 0)
        {
            var neighborhood = await _context.Neighborhoods.FindAsync(address.NeighborhoodId);
            if (neighborhood != null)
            {
                address.DeliveryFee = neighborhood.DeliveryFee;
            }
        }

        _context.Addresses.Add(address);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(address.Id) ?? address;
    }

    public async Task<Address> UpdateAsync(Address address)
    {
        // Update delivery fee if neighborhood changed
        var neighborhood = await _context.Neighborhoods.FindAsync(address.NeighborhoodId);
        if (neighborhood != null)
        {
            address.DeliveryFee = neighborhood.DeliveryFee;
        }

        _context.Addresses.Update(address);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(address.Id) ?? address;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var address = await _context.Addresses.FindAsync(id);
        if (address == null)
            return false;

        // Check if address is being used in orders
        var hasOrders = await _context.Orders.AnyAsync(o => o.AddressId == id);
        if (hasOrders)
        {
            // Don't delete if used in orders, just mark as inactive if you add that field
            return false;
        }

        _context.Addresses.Remove(address);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(int id)
    {
        return await _context.Addresses.AnyAsync(a => a.Id == id);
    }

    public async Task<bool> SetPrimaryAddressAsync(int customerId, int addressId)
    {
        // First unset all primary addresses for the customer
        await UnsetPrimaryAddressesAsync(customerId);
        
        // Then set the specified address as primary
        var address = await _context.Addresses
            .FirstOrDefaultAsync(a => a.Id == addressId && a.CustomerId == customerId);
        
        if (address == null)
            return false;
        
        address.IsPrimary = true;
        await _context.SaveChangesAsync();
        
        return true;
    }

    public async Task<bool> UnsetPrimaryAddressesAsync(int customerId)
    {
        var addresses = await _context.Addresses
            .Where(a => a.CustomerId == customerId && a.IsPrimary)
            .ToListAsync();
        
        if (!addresses.Any())
            return true;
        
        foreach (var address in addresses)
        {
            address.IsPrimary = false;
        }
        
        await _context.SaveChangesAsync();
        return true;
    }
}