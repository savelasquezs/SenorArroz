using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly ApplicationDbContext _context;

    public RefreshTokenRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<RefreshToken?> GetByTokenAsync(string token)
    {
        return await _context.RefreshTokens
            .Include(rt => rt.User)
            .ThenInclude(u => u.Branch)
            .FirstOrDefaultAsync(rt => rt.Token == token);
    }

    public async Task<RefreshToken?> GetActiveByUserIdAsync(int userId)
    {
        return await _context.RefreshTokens
            .Include(rt => rt.User)
            .ThenInclude(u => u.Branch)
            .FirstOrDefaultAsync(rt => rt.UserId == userId &&
                                      !rt.IsRevoked &&
                                      rt.ExpiresAt > DateTime.UtcNow);
    }

    public async Task<IEnumerable<RefreshToken>> GetAllByUserIdAsync(int userId)
    {
        return await _context.RefreshTokens
            .Where(rt => rt.UserId == userId)
            .OrderByDescending(rt => rt.CreatedAt)
            .ToListAsync();
    }

    public async Task AddAsync(RefreshToken refreshToken)
    {
        await _context.RefreshTokens.AddAsync(refreshToken);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(RefreshToken refreshToken)
    {
        _context.RefreshTokens.Update(refreshToken);
        await _context.SaveChangesAsync();
    }

    public async Task RevokeAllByUserIdAsync(int userId, string ipAddress)
    {
        var activeTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && !rt.IsRevoked)
            .ToListAsync();

        foreach (var token in activeTokens)
        {
            token.Revoke(ipAddress);
        }

        await _context.SaveChangesAsync();
    }

    public async Task DeleteExpiredTokensAsync()
    {
        var expiredTokens = await _context.RefreshTokens
            .Where(rt => rt.ExpiresAt < DateTime.UtcNow || rt.IsRevoked)
            .Where(rt => rt.CreatedAt < DateTime.UtcNow.AddDays(-30)) // Keep revoked tokens for 30 days for audit
            .ToListAsync();

        _context.RefreshTokens.RemoveRange(expiredTokens);
        await _context.SaveChangesAsync();
    }
}