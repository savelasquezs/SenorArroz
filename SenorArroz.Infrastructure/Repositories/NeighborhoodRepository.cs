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
            .Include(n => n.Branch)
            .Where(n => n.BranchId == branchId)
            .OrderBy(n => n.Name)
            .ToListAsync();
    }

    public async Task<Neighborhood?> GetByIdAsync(int id)
    {
        return await _context.Neighborhoods
            .Include(n => n.Branch)
            .FirstOrDefaultAsync(n => n.Id == id);
    }

    public async Task<Neighborhood> CreateAsync(Neighborhood neighborhood)
    {
        neighborhood.CreatedAt = DateTime.UtcNow;
        neighborhood.UpdatedAt = DateTime.UtcNow;

        _context.Neighborhoods.Add(neighborhood);
        await _context.SaveChangesAsync();

        return await GetByIdAsync(neighborhood.Id) ?? neighborhood;
    }

    public async Task<Neighborhood> UpdateAsync(Neighborhood neighborhood)
    {
        neighborhood.UpdatedAt = DateTime.UtcNow;

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
}