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

    public async Task<User?> GetUserByEmailAsync(string email)
    {
        return await _context.Users
            .Include(u => u.Branch)
            .FirstOrDefaultAsync(u => u.Email == email && u.Active);
    }

    public async Task<User?> GetUserByIdWithBranchAsync(int userId)
    {
        return await _context.Users
            .Include(u => u.Branch)
            .FirstOrDefaultAsync(u => u.Id == userId && u.Active);
    }

    public async Task<bool> ValidatePasswordAsync(User user, string password)
    {
        return await Task.FromResult(_passwordService.VerifyPassword(password, user.PasswordHash));
    }

    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null || !user.Active)
            return false;

        if (!_passwordService.VerifyPassword(currentPassword, user.PasswordHash))
            return false;

        user.PasswordHash = _passwordService.HashPassword(newPassword);
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }
}