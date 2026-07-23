using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class RefreshToken : BaseEntity
{
    public int UserId { get; set; }
    public Guid? SessionId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; } = false;
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByToken { get; set; }
    public string? RevokedByIp { get; set; }

    // Navigation properties
    public virtual User User { get; set; } = null!;

    public bool IsExpiredAt(DateTime utcNow) => utcNow >= ExpiresAt;

    public bool IsActiveAt(DateTime utcNow) => !IsRevoked && !IsExpiredAt(utcNow);

    public void Revoke(string ipAddress, DateTime utcNow, string? replacedByToken = null)
    {
        IsRevoked = true;
        RevokedAt = utcNow;
        RevokedByIp = ipAddress;
        ReplacedByToken = replacedByToken;
    }
}
