using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Repositories;

public class PasswordResetRepository : IPasswordResetRepository
{
    private readonly ApplicationDbContext _context;
    private readonly IClock _clock;
    private readonly ITenantExecutionContext? _tenantExecutionContext;

    public PasswordResetRepository(ApplicationDbContext context, IClock clock, ITenantExecutionContext? tenantExecutionContext = null)
    {
        _context = context;
        _clock = clock;
        _tenantExecutionContext = tenantExecutionContext;
    }

    public async Task<PasswordResetToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        using var scope = _tenantExecutionContext?.BeginSystemScope();
        return await _context.PasswordResetTokens
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(prt => prt.User)
            .FirstOrDefaultAsync(prt => prt.Token == token, cancellationToken);
    }

    public async Task<PasswordResetToken?> GetValidTokenByUserIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        return await _context.PasswordResetTokens
            .AsNoTracking()
            .Include(prt => prt.User)
            .FirstOrDefaultAsync(prt => prt.UserId == userId &&
                                      !prt.IsUsed &&
                                      prt.ExpiresAt > now, cancellationToken);
    }

    public async Task<IEnumerable<PasswordResetToken>> GetByUserIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await _context.PasswordResetTokens
            .AsNoTracking()
            .Include(prt => prt.User)
            .Where(prt => prt.UserId == userId)
            .OrderByDescending(prt => prt.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<PasswordResetToken> CreateAsync(PasswordResetToken passwordResetToken, CancellationToken cancellationToken = default)
    {
        _context.PasswordResetTokens.Add(passwordResetToken);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByTokenAsync(passwordResetToken.Token, cancellationToken) ?? passwordResetToken;
    }

    public async Task UpdateAsync(PasswordResetToken passwordResetToken, CancellationToken cancellationToken = default)
    {
        using var scope = _tenantExecutionContext?.BeginSystemScope();
        // GetByTokenAsync returns a detached graph that includes User. Attaching that
        // graph with Update() can conflict with another tracked User instance during
        // the password-reset flow. Update only the token's mutable scalar fields.
        var existingToken = await _context.PasswordResetTokens
            .FirstOrDefaultAsync(x => x.Id == passwordResetToken.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Password reset token {passwordResetToken.Id} no longer exists.");

        existingToken.IsUsed = passwordResetToken.IsUsed;
        existingToken.UsedAt = passwordResetToken.UsedAt;
        existingToken.UsedByIp = passwordResetToken.UsedByIp;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task InvalidateAllUserTokensAsync(int userId, CancellationToken cancellationToken = default)
    {
        using var scope = _tenantExecutionContext?.BeginSystemScope();
        var tokens = await _context.PasswordResetTokens
            .Where(prt => prt.UserId == userId && !prt.IsUsed)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.MarkAsUsed("system_invalidation", _clock.UtcNow);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteExpiredTokensAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var expiredTokens = await _context.PasswordResetTokens
            .Where(prt => prt.ExpiresAt < now || prt.IsUsed)
            .Where(prt => prt.CreatedAt < now.AddDays(-7))
            .ToListAsync(cancellationToken);

        _context.PasswordResetTokens.RemoveRange(expiredTokens);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
