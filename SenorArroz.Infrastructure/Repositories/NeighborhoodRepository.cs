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

    public async Task<IEnumerable<Neighborhood>> GetByBranchIdAsync(int branchId)
    {
        return await _context.Neighborhoods
            .AsNoTracking()
            .Include(n => n.Branch)
            .Where(n => n.BranchId == branchId)
            .OrderBy(n => n.Name)
            .ToListAsync();
    }

    public async Task<Neighborhood?> GetByIdAsync(int id)
    {
        return await _context.Neighborhoods
            .AsNoTracking()
            .Include(n => n.Branch)
            .FirstOrDefaultAsync(n => n.Id == id);
    }

    public async Task<Neighborhood> CreateAsync(Neighborhood neighborhood)
    {
        _context.Neighborhoods.Add(neighborhood);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(neighborhood.Id) ?? neighborhood;
    }

    public async Task<Neighborhood> UpdateAsync(Neighborhood neighborhood)
    {
        _context.Neighborhoods.Update(neighborhood);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(neighborhood.Id) ?? neighborhood;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var neighborhood = await _context.Neighborhoods.FindAsync(id);
        if (neighborhood == null)
            return false;

        // Check if neighborhood has addresses
        var hasAddresses = await _context.Addresses.AnyAsync(a => a.NeighborhoodId == id);
        if (hasAddresses)
            return false; // Can't delete if has addresses

        _context.Neighborhoods.Remove(neighborhood);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExistsAsync(int id)
    {
        return await _context.Neighborhoods.AnyAsync(n => n.Id == id);
    }

    public async Task<bool> NameExistsAsync(string name, int branchId, int? excludeId = null)
    {
        var query = _context.Neighborhoods
            .Where(n => n.Name.ToLower() == name.ToLower() && n.BranchId == branchId);

        if (excludeId.HasValue)
        {
            query = query.Where(n => n.Id != excludeId.Value);
        }

        return await query.AnyAsync();
    }
    public async Task<int> GetTotalCustomersAsync(int neighborhoodId)
    {
        return await _context.Customers
            .CountAsync(c => _context.Addresses
                .Any(a => a.CustomerId == c.Id && a.NeighborhoodId == neighborhoodId));
    }

    public async Task<int> GetTotalAddressesAsync(int neighborhoodId)
    {
        return await _context.Addresses
            .CountAsync(a => a.NeighborhoodId == neighborhoodId);
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