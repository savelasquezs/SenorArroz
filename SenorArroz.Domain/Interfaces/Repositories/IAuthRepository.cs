using SenorArroz.Domain.Entities;

namespace SenorArroz.Domain.Interfaces.Repositories;

public interface IAuthRepository
{
    Task<User?> GetUserByEmailAsync(string email);
    Task<User?> GetUserByIdWithBranchAsync(int userId);
    Task<bool> ValidatePasswordAsync(User user, string password);
    Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword);
    Task<bool> UpdateUserPasswordAsync(User user, string newPasswordHash);
}