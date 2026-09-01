using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public class StorefrontCustomerAuthChallenge : BaseEntity
{
    public int TenantId { get; set; }
    public Guid PublicId { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string CodeHmac { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime ResendAvailableAt { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 5;
    public DateTime? SentAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public string? SessionTokenHash { get; set; }
    public DateTime? SessionExpiresAt { get; set; }
    public string RequestIpHash { get; set; } = string.Empty;
}
