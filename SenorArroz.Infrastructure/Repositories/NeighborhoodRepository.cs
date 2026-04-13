using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Repositories;

public class NeighborhoodRepository : INeighborhoodRepository
{
    private readonly ApplicationDbContext _context;

    public NeighborhoodRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Neighborhood>> GetByBranchIdAsync(int branchId, CancellationToken cancellationToken = default)
    {
        return await _context.Neighborhoods
            .AsNoTracking()
            .Include(n => n.Branch)
            .Where(n => n.BranchId == branchId)
            .OrderBy(n => n.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Neighborhood?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Neighborhoods
            .AsNoTracking()
            .Include(n => n.Branch)
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
    }

    public async Task<Neighborhood> CreateAsync(Neighborhood neighborhood, CancellationToken cancellationToken = default)
    {
        _context.Neighborhoods.Add(neighborhood);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(neighborhood.Id, cancellationToken) ?? neighborhood;
    }

    public async Task<Neighborhood> UpdateAsync(Neighborhood neighborhood, CancellationToken cancellationToken = default)
    {
        _context.Neighborhoods.Update(neighborhood);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(neighborhood.Id, cancellationToken) ?? neighborhood;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var neighborhood = await _context.Neighborhoods.FindAsync([id], cancellationToken);
        if (neighborhood == null)
            return false;

        var hasAddresses = await _context.Addresses.AnyAsync(a => a.NeighborhoodId == id, cancellationToken);
        if (hasAddresses)
            return false;

        _context.Neighborhoods.Remove(neighborhood);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Neighborhoods.AnyAsync(n => n.Id == id, cancellationToken);
    }

    public async Task<bool> NameExistsAsync(string name, int branchId, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Neighborhoods
            .Where(n => n.Name.ToLower() == name.ToLower() && n.BranchId == branchId);

        if (excludeId.HasValue)
            query = query.Where(n => n.Id != excludeId.Value);

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<int> GetTotalCustomersAsync(int neighborhoodId, CancellationToken cancellationToken = default)
    {
        return await _context.Customers
            .CountAsync(c => _context.Addresses
                .Any(a => a.CustomerId == c.Id && a.NeighborhoodId == neighborhoodId), cancellationToken);
    }

    public async Task<int> GetTotalAddressesAsync(int neighborhoodId, CancellationToken cancellationToken = default)
    {
        return await _context.Addresses
            .CountAsync(a => a.NeighborhoodId == neighborhoodId, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, (int TotalCustomers, int TotalAddresses)>> GetNeighborhoodStatsBulkAsync(
        IReadOnlyCollection<int> neighborhoodIds,
        CancellationToken cancellationToken = default)
    {
        if (neighborhoodIds == null || neighborhoodIds.Count == 0)
            return new Dictionary<int, (int, int)>();

        var ids = neighborhoodIds.Distinct().ToList();

        var addressRows = await _context.Addresses.AsNoTracking()
            .Where(a => ids.Contains(a.NeighborhoodId))
            .GroupBy(a => a.NeighborhoodId)
            .Select(g => new { NeighborhoodId = g.Key, AddressCount = g.Count() })
            .ToListAsync(cancellationToken);

        var customerRows = await _context.Addresses.AsNoTracking()
            .Where(a => ids.Contains(a.NeighborhoodId))
            .GroupBy(a => a.NeighborhoodId)
            .Select(g => new
            {
                NeighborhoodId = g.Key,
                CustomerCount = g.Select(x => x.CustomerId).Distinct().Count()
            })
            .ToListAsync(cancellationToken);

        var addrByHood = addressRows.ToDictionary(x => x.NeighborhoodId, x => x.AddressCount);
        var custByHood = customerRows.ToDictionary(x => x.NeighborhoodId, x => x.CustomerCount);

        return ids.ToDictionary(
            id => id,
            id => (custByHood.GetValueOrDefault(id), addrByHood.GetValueOrDefault(id)));
    }
}
