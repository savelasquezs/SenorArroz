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
