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

    public PasswordResetRepository(ApplicationDbContext context, IClock clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<PasswordResetToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        return await _context.PasswordResetTokens
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
        _context.PasswordResetTokens.Update(passwordResetToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task InvalidateAllUserTokensAsync(int userId, CancellationToken cancellationToken = default)
    {
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
