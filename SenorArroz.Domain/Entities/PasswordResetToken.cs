using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class PasswordResetToken : BaseEntity
{
    public int UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; } = false;
    public DateTime? UsedAt { get; set; }
    public string? UsedByIp { get; set; }
    public string Email { get; set; } = string.Empty; // Store email for verification

    // Navigation properties
    public virtual User User { get; set; } = null!;

    public bool IsExpiredAt(DateTime utcNow) => utcNow >= ExpiresAt;

    public bool IsValidAt(DateTime utcNow) => !IsUsed && !IsExpiredAt(utcNow);

    public void MarkAsUsed(string ipAddress, DateTime utcNow)
    {
        IsUsed = true;
        UsedAt = utcNow;
        UsedByIp = ipAddress;
    }

    public static PasswordResetToken Create(int userId, string email, int expirationMinutes, DateTime utcNow)
    {
        return new PasswordResetToken
        {
            UserId = userId,
            Email = email,
            Token = GenerateSecureToken(),
            ExpiresAt = utcNow.AddMinutes(expirationMinutes)
        };
    }

    private static string GenerateSecureToken()
    {
        // Generate a secure random token
        var bytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
    }
}
