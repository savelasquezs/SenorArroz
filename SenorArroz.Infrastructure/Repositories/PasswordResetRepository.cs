using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Repositories;

public class PasswordResetRepository : IPasswordResetRepository
{
    private readonly ApplicationDbContext _context;

    public PasswordResetRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PasswordResetToken?> GetByTokenAsync(string token)
    {
        return await _context.PasswordResetTokens
            .Include(prt => prt.User)
            .FirstOrDefaultAsync(prt => prt.Token == token);
    }

    public async Task<PasswordResetToken?> GetValidTokenByUserIdAsync(int userId)
    {
        return await _context.PasswordResetTokens
            .Include(prt => prt.User)
            .FirstOrDefaultAsync(prt => prt.UserId == userId &&
                                      !prt.IsUsed &&
                                      prt.ExpiresAt > DateTime.UtcNow);
    }

    public async Task<IEnumerable<PasswordResetToken>> GetByUserIdAsync(int userId)
    {
        return await _context.PasswordResetTokens
            .Include(prt => prt.User)
            .Where(prt => prt.UserId == userId)
            .OrderByDescending(prt => prt.CreatedAt)
            .ToListAsync();
    }

    public async Task<PasswordResetToken> CreateAsync(PasswordResetToken passwordResetToken)
    {
        passwordResetToken.CreatedAt = DateTime.UtcNow;
        passwordResetToken.UpdatedAt = DateTime.UtcNow;

        _context.PasswordResetTokens.Add(passwordResetToken);
        await _context.SaveChangesAsync();

        return await GetByTokenAsync(passwordResetToken.Token) ?? passwordResetToken;
    }

    public async Task UpdateAsync(PasswordResetToken passwordResetToken)
    {
        passwordResetToken.UpdatedAt = DateTime.UtcNow;

        _context.PasswordResetTokens.Update(passwordResetToken);
        await _context.SaveChangesAsync();
    }

    public async Task InvalidateAllUserTokensAsync(int userId)
    {
        var tokens = await _context.PasswordResetTokens
            .Where(prt => prt.UserId == userId && !prt.IsUsed)
            .ToListAsync();

        foreach (var token in tokens)
        {
            token.MarkAsUsed("system_invalidation");
        }

        await _context.SaveChangesAsync();
    }

    public async Task DeleteExpiredTokensAsync()
    {
        var expiredTokens = await _context.PasswordResetTokens
            .Where(prt => prt.ExpiresAt < DateTime.UtcNow || prt.IsUsed)
            .Where(prt => prt.CreatedAt < DateTime.UtcNow.AddDays(-7)) // Keep for 7 days for audit
            .ToListAsync();

        _context.PasswordResetTokens.RemoveRange(expiredTokens);
        await _context.SaveChangesAsync();
    }
}