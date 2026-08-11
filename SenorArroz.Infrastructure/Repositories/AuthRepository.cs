using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Interfaces.Services;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Repositories;

public class AuthRepository : IAuthRepository
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordService _passwordService;

    public AuthRepository(ApplicationDbContext context, IPasswordService passwordService)
    {
        _context = context;
        _passwordService = passwordService;
    }

    public async Task<User?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .AsNoTracking()
            .Include(u => u.Branch)
            .FirstOrDefaultAsync(u => u.Email == email && u.Active, cancellationToken);
    }

    public async Task<User?> GetUserByIdWithBranchAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .AsNoTracking()
            .Include(u => u.Branch)
            .FirstOrDefaultAsync(u => u.Id == userId && u.Active, cancellationToken);
    }

    public Task<bool> IsSessionCurrentAsync(
        int userId,
        Guid? sessionId,
        CancellationToken cancellationToken = default)
    {
        return _context.Users.AsNoTracking().AnyAsync(
            user => user.Id == userId
                    && user.Active
                    && user.Role == Domain.Enums.UserRole.Deliveryman
                    && user.ActiveSessionId == sessionId,
            cancellationToken);
    }

    public Task<bool> CanDeliverymanAccessWebAsync(
        int userId,
        CancellationToken cancellationToken = default) =>
        _context.Users.AsNoTracking().AnyAsync(
            user => user.Id == userId
                    && user.Active
                    && user.Role == Domain.Enums.UserRole.Deliveryman
                    && user.WebAccessEnabled,
            cancellationToken);

    public async Task EndSessionIfCurrentAsync(
        int userId,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(
            candidate => candidate.Id == userId
                         && candidate.ActiveSessionId == sessionId,
            cancellationToken);
        if (user is null) return;

        user.ActiveSessionId = null;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ValidatePasswordAsync(User user, string password, CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(_passwordService.VerifyPassword(password, user.PasswordHash));
    }

    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FindAsync([userId], cancellationToken);
        if (user == null || !user.Active)
            return false;

        if (!_passwordService.VerifyPassword(currentPassword, user.PasswordHash))
            return false;

        user.PasswordHash = _passwordService.HashPassword(newPassword);

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateUserPasswordAsync(User user, string newPasswordHash, CancellationToken cancellationToken = default)
    {
        try
        {
            var existingUser = await _context.Users.FindAsync([user.Id], cancellationToken);
            if (existingUser == null || !existingUser.Active)
                return false;

            existingUser.PasswordHash = newPasswordHash;

            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
