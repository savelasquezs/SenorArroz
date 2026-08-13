using SenorArroz.Domain.Enums;

namespace SenorArroz.Domain.Entities;

public sealed class Tenant
{
    public int Id { get; set; }
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string? BillingAddress { get; set; }
    public TenantStatus Status { get; set; } = TenantStatus.Draft;
    public string? StatusReason { get; set; }
    public long AccessVersion { get; set; } = 1;
    public DateTime? SuspendedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<Branch> Branches { get; set; } = new List<Branch>();
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<TenantSubscription> Subscriptions { get; set; } = new List<TenantSubscription>();
    public ICollection<TenantAddon> Addons { get; set; } = new List<TenantAddon>();
}

public sealed class PlatformUser
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<PlatformSession> Sessions { get; set; } = new List<PlatformSession>();
    public ICollection<PlatformTrustedDevice> TrustedDevices { get; set; } = new List<PlatformTrustedDevice>();
}

public sealed class PlatformSession
{
    public long Id { get; set; }
    public int PlatformUserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string CsrfTokenHash { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public PlatformUser PlatformUser { get; set; } = null!;
}

public sealed class PlatformOtpChallenge
{
    public long Id { get; set; }
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public int PlatformUserId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 5;
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public PlatformUser PlatformUser { get; set; } = null!;
}

public sealed class PlatformTrustedDevice
{
    public long Id { get; set; }
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public int PlatformUserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public PlatformUser PlatformUser { get; set; } = null!;
}

public sealed class PlatformSetting
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string ValueJson { get; set; } = "null";
    public DateTime UpdatedAt { get; set; }
}

public sealed class SaasModule
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public sealed class SaasAddon
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public int DisplayOrder { get; set; }
}

public sealed class SaasPlan
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<SaasPlanVersion> Versions { get; set; } = new List<SaasPlanVersion>();
}

public sealed class SaasPlanVersion
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public int VersionNumber { get; set; }
    public PlanVersionStatus Status { get; set; } = PlanVersionStatus.Draft;
    public string Currency { get; set; } = "COP";
    public decimal? MonthlyPrice { get; set; }
    public decimal? AnnualPrice { get; set; }
    public int? BranchLimit { get; set; }
    public int? UserLimit { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public SaasPlan Plan { get; set; } = null!;
    public ICollection<SaasPlanVersionModule> Modules { get; set; } = new List<SaasPlanVersionModule>();
}

public sealed class SaasPlanVersionModule
{
    public int PlanVersionId { get; set; }
    public int ModuleId { get; set; }
    public SaasPlanVersion PlanVersion { get; set; } = null!;
    public SaasModule Module { get; set; } = null!;
}

public sealed class TenantSubscription
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int PlanVersionId { get; set; }
    public TenantSubscriptionStatus Status { get; set; } = TenantSubscriptionStatus.Active;
    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public SaasPlanVersion PlanVersion { get; set; } = null!;
}

public sealed class TenantAddon
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int AddonId { get; set; }
    public bool Active { get; set; }
    public DateTime? EnabledAt { get; set; }
    public DateTime? DisabledAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public SaasAddon Addon { get; set; } = null!;
}

public sealed class TenantInvitation
{
    public long Id { get; set; }
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }
    public int BranchId { get; set; }
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
    public User User { get; set; } = null!;
}

public sealed class PlatformAuditLog
{
    public long Id { get; set; }
    public int? PlatformUserId { get; set; }
    public string ActorIdentifier { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string BeforeJson { get; set; } = "null";
    public string AfterJson { get; set; } = "null";
    public string IpAddress { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public PlatformUser? PlatformUser { get; set; }
}

public sealed class TenantUsageMonthly
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public DateOnly Month { get; set; }
    public long Orders { get; set; }
    public long StorageBytes { get; set; }
    public long AiInputTokens { get; set; }
    public long AiOutputTokens { get; set; }
    public decimal AiEstimatedCostUsd { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Tenant Tenant { get; set; } = null!;
}
