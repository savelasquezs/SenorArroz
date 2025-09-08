using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class RefreshToken : BaseEntity
{
    public int UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; } = false;
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByToken { get; set; }
    public string? RevokedByIp { get; set; }

    // Navigation properties
    public virtual User User { get; set; } = null!;

    // Methods
    public bool IsActive => !IsRevoked && !IsExpired;
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    public void Revoke(string ipAddress, string? replacedByToken = null)
    {
        IsRevoked = true;
        RevokedAt = DateTime.UtcNow;
        RevokedByIp = ipAddress;
        ReplacedByToken = replacedByToken;
    }
}