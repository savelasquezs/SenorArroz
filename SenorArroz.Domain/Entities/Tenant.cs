using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public sealed class Tenant : BaseEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    public string? StatusReason { get; set; }
    public long AccessVersion { get; set; } = 1;
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string? BillingAddress { get; set; }
    public DateTime? SuspendedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public ICollection<Branch> Branches { get; set; } = new List<Branch>();
    public ICollection<Customer> Customers { get; set; } = new List<Customer>();
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();
    public ICollection<UserDeviceToken> UserDeviceTokens { get; set; } = new List<UserDeviceToken>();
}
