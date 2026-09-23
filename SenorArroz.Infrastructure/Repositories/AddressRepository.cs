using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Repositories;

public class AddressRepository : IAddressRepository
{
    private readonly ApplicationDbContext _context;
    private readonly int _tenantId;

    public AddressRepository(ApplicationDbContext context, ICurrentTenant? currentTenant = null)
    {
        _context = context;
        _tenantId = currentTenant?.TenantId ?? context.CurrentTenantIdForFilter;
    }

    public async Task<IEnumerable<Address>> GetByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return await _context.Addresses
            .AsNoTracking()
            .Include(a => a.Neighborhood)
            .Include(a => a.BranchServices).ThenInclude(x => x.Neighborhood)
            .Include(a => a.BranchServices).ThenInclude(x => x.Branch)
            .Include(a => a.Customer)
            .Where(a => a.TenantId == _tenantId && a.CustomerId == customerId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Address?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Addresses
            .AsNoTracking()
            .Include(a => a.Neighborhood)
            .Include(a => a.BranchServices).ThenInclude(x => x.Neighborhood)
            .Include(a => a.BranchServices).ThenInclude(x => x.Branch)
            .Include(a => a.Customer)
            .FirstOrDefaultAsync(a => a.TenantId == _tenantId && a.Id == id, cancellationToken);
    }

    public async Task<Address?> GetPrimaryByCustomerIdAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return await _context.Addresses
            .AsNoTracking()
            .Include(a => a.Neighborhood)
            .Include(a => a.BranchServices).ThenInclude(x => x.Neighborhood)
            .Include(a => a.BranchServices).ThenInclude(x => x.Branch)
            .Include(a => a.Customer)
            .Where(a => a.TenantId == _tenantId && a.CustomerId == customerId && a.IsPrimary)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Address> CreateAsync(Address address, CancellationToken cancellationToken = default)
    {
        if (address.TenantId != _tenantId)
            throw new InvalidOperationException("No se puede crear una dirección para otro tenant.");
        if (address.DeliveryFee == 0 && address.NeighborhoodId.HasValue)
        {
            var neighborhood = await _context.Neighborhoods.FindAsync([address.NeighborhoodId.Value], cancellationToken);
            if (neighborhood != null)
            {
                address.DeliveryFee = neighborhood.DeliveryFee;
            }
        }

        _context.Addresses.Add(address);
        if (address.NeighborhoodId is int neighborhoodId)
        {
            var neighborhood = await _context.Neighborhoods.AsNoTracking().FirstAsync(x => x.TenantId == _tenantId && x.Id == neighborhoodId, cancellationToken);
            address.TenantId = neighborhood.TenantId;
            address.BranchServices.Add(new AddressBranch
            {
                TenantId = neighborhood.TenantId,
                BranchId = neighborhood.BranchId,
                NeighborhoodId = neighborhood.Id,
                DeliveryFee = address.DeliveryFee,
                IsCovered = true,
                ValidatedAt = address.ValidatedAt
            });
        }
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(address.Id, cancellationToken) ?? address;
    }

    public async Task<Address> UpdateAsync(Address address, CancellationToken cancellationToken = default)
    {
        // La entidad proviene de una lectura AsNoTracking; los servicios se actualizan de forma
        // explícita para no adjuntar una segunda instancia del mismo AddressBranch.
        address.BranchServices.Clear();
        if (address.NeighborhoodId is int neighborhoodId)
        {
            var neighborhood = await _context.Neighborhoods.AsNoTracking().FirstAsync(x => x.TenantId == _tenantId && x.Id == neighborhoodId, cancellationToken);
            var service = await _context.AddressBranches.FirstOrDefaultAsync(
                x => x.TenantId == address.TenantId && x.AddressId == address.Id && x.BranchId == neighborhood.BranchId,
                cancellationToken);
            if (service is null)
                _context.AddressBranches.Add(new AddressBranch
                {
                    TenantId = address.TenantId,
                    AddressId = address.Id,
                    BranchId = neighborhood.BranchId,
                    NeighborhoodId = neighborhood.Id,
                    DeliveryFee = address.DeliveryFee,
                    IsCovered = true,
                    ValidatedAt = address.ValidatedAt
                });
            else
            {
                service.NeighborhoodId = neighborhood.Id;
                service.DeliveryFee = address.DeliveryFee;
                service.IsCovered = true;
                service.ValidatedAt = address.ValidatedAt ?? service.ValidatedAt;
            }
        }
        _context.Addresses.Update(address);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(address.Id, cancellationToken) ?? address;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var address = await _context.Addresses.FirstOrDefaultAsync(x => x.TenantId == _tenantId && x.Id == id, cancellationToken);
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
        return await _context.Addresses.AnyAsync(a => a.TenantId == _tenantId && a.Id == id, cancellationToken);
    }

    public async Task<bool> SetPrimaryAddressAsync(int customerId, int addressId, CancellationToken cancellationToken = default)
    {
        await UnsetPrimaryAddressesAsync(customerId, cancellationToken);

        var address = await _context.Addresses
            .FirstOrDefaultAsync(a => a.TenantId == _tenantId && a.Id == addressId && a.CustomerId == customerId, cancellationToken);

        if (address == null)
            return false;

        address.IsPrimary = true;
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> UnsetPrimaryAddressesAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var addresses = await _context.Addresses
            .Where(a => a.TenantId == _tenantId && a.CustomerId == customerId && a.IsPrimary)
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
