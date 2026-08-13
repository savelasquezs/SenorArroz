using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly ApplicationDbContext _context;
    private readonly IClock _clock;
    private readonly ITenantExecutionContext? _tenantExecutionContext;

    public RefreshTokenRepository(ApplicationDbContext context, IClock clock, ITenantExecutionContext? tenantExecutionContext = null)
    {
        _context = context;
        _clock = clock;
        _tenantExecutionContext = tenantExecutionContext;
    }

    public async Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        using var scope = _tenantExecutionContext?.BeginSystemScope();
        return await _context.RefreshTokens
            .IgnoreQueryFilters()
            .Include(rt => rt.User)
            .ThenInclude(u => u.Branch)
            .Include(rt => rt.User).ThenInclude(u => u.Tenant)
            .FirstOrDefaultAsync(rt => rt.Token == token, cancellationToken);
    }

    public async Task<RefreshToken?> GetActiveByUserIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        return await _context.RefreshTokens
            .Include(rt => rt.User)
            .ThenInclude(u => u.Branch)
            .FirstOrDefaultAsync(rt => rt.UserId == userId &&
                                      !rt.IsRevoked &&
                                      rt.ExpiresAt > now, cancellationToken);
    }

    public async Task<IEnumerable<RefreshToken>> GetAllByUserIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await _context.RefreshTokens
            .AsNoTracking()
            .Where(rt => rt.UserId == userId)
            .OrderByDescending(rt => rt.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default)
    {
        using var scope = _tenantExecutionContext?.BeginSystemScope();
        await _context.RefreshTokens.AddAsync(refreshToken, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default)
    {
        _context.RefreshTokens.Update(refreshToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAllByUserIdAsync(int userId, string ipAddress, CancellationToken cancellationToken = default)
    {
        using var scope = _tenantExecutionContext?.BeginSystemScope();
        var activeTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && !rt.IsRevoked)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.Revoke(ipAddress, _clock.UtcNow);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteExpiredTokensAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var expiredTokens = await _context.RefreshTokens
            .Where(rt => rt.ExpiresAt < now || rt.IsRevoked)
            .Where(rt => rt.CreatedAt < now.AddDays(-30))
            .ToListAsync(cancellationToken);

        _context.RefreshTokens.RemoveRange(expiredTokens);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
