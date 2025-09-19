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

    // Methods
    public bool IsValid => !IsUsed && !IsExpired;
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    public void MarkAsUsed(string ipAddress)
    {
        IsUsed = true;
        UsedAt = DateTime.UtcNow;
        UsedByIp = ipAddress;
    }

    public static PasswordResetToken Create(int userId, string email, int expirationMinutes = 60)
    {
        return new PasswordResetToken
        {
            UserId = userId,
            Email = email,
            Token = GenerateSecureToken(),
            ExpiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes),
            CreatedAt = DateTime.UtcNow
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