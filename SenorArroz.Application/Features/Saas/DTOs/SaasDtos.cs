namespace SenorArroz.Application.Features.Saas.DTOs;

public sealed record TenantContextDto(
    int TenantId,
    Guid PublicId,
    string Name,
    string Slug,
    string Status,
    string PlanCode,
    string PlanName,
    int PlanVersion,
    int? BranchLimit,
    int? UserLimit,
    int BranchCount,
    int UserCount,
    IReadOnlyList<string> Modules,
    IReadOnlyList<string> Addons,
    TenantUsageDto Usage);

public sealed record TenantUsageDto(long Orders, long StorageBytes, long AiInputTokens, long AiOutputTokens, decimal AiEstimatedCostUsd);

public sealed record PlatformRequestContext(string IpAddress, string UserAgent, string CorrelationId);
public sealed record PlatformLoginRequest(string Email, string Password, string? DeviceName);
public sealed record PlatformVerifyOtpRequest(Guid ChallengeId, string Code, string? DeviceName);
public sealed record PlatformSessionDto(int Id, string Name, string Email);
public sealed record PlatformTrustedDeviceDto(Guid PublicId, string Name, string UserAgent, string IpAddress, DateTime LastUsedAt, DateTime ExpiresAt);

public sealed class PlatformLoginResult
{
    public bool OtpRequired { get; init; }
    public Guid? ChallengeId { get; init; }
    public DateTime? ChallengeExpiresAt { get; init; }
    public PlatformSessionDto? User { get; init; }
    public string? SessionToken { get; init; }
    public string? CsrfToken { get; init; }
    public string? TrustedDeviceToken { get; init; }
}

public sealed record PlatformTenantListItemDto(
    int Id,
    Guid PublicId,
    string Name,
    string Slug,
    string Status,
    string ContactEmail,
    string PlanName,
    int BranchCount,
    int UserCount,
    DateTime CreatedAt);

public sealed record PlatformTenantDetailDto(
    int Id,
    Guid PublicId,
    string Name,
    string Slug,
    string ContactName,
    string ContactEmail,
    string? ContactPhone,
    string? LegalName,
    string? TaxId,
    string? BillingAddress,
    string Status,
    string? StatusReason,
    PlatformPlanVersionDto Plan,
    IReadOnlyList<string> Addons,
    IReadOnlyList<PlatformTenantBranchDto> Branches,
    IReadOnlyList<PlatformTenantUserDto> Users,
    TenantUsageDto Usage,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record PlatformTenantBranchDto(int Id, string Name, string Address, bool Active);
public sealed record PlatformTenantUserDto(int Id, string Name, string Email, string Role, bool Active);

public sealed class CreatePlatformTenantRequest
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string? BillingAddress { get; set; }
    public int PlanVersionId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string BranchAddress { get; set; } = string.Empty;
    public string BranchPhone { get; set; } = string.Empty;
    public string AdminName { get; set; } = string.Empty;
    public string AdminEmail { get; set; } = string.Empty;
    public string AdminPhone { get; set; } = string.Empty;
    public IReadOnlyList<string> Addons { get; set; } = [];
}

public sealed class UpdatePlatformTenantRequest
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string? BillingAddress { get; set; }
}

public sealed record ChangeTenantStatusRequest(string Status, string? Reason);
public sealed record ChangeTenantSubscriptionRequest(int PlanVersionId);
public sealed record AcceptTenantInvitationRequest(Guid InvitationId, string Token, string Password);

public sealed record PlatformCatalogItemDto(int Id, string Code, string Name, string Description, string Category, bool Active, int DisplayOrder);
public sealed record UpsertPlatformCatalogItemRequest(string Code, string Name, string Description, string Category, bool Active, int DisplayOrder);

public sealed record PlatformPlanDto(int Id, string Code, string Name, string Description, bool Active, IReadOnlyList<PlatformPlanVersionDto> Versions);

public sealed record PlatformPlanVersionDto(
    int Id,
    int VersionNumber,
    string Status,
    string Currency,
    decimal? MonthlyPrice,
    decimal? AnnualPrice,
    int? BranchLimit,
    int? UserLimit,
    IReadOnlyList<string> Modules,
    DateTime? PublishedAt,
    DateTime CreatedAt);

public sealed record CreatePlatformPlanRequest(string Code, string Name, string Description);

public sealed class UpsertPlanVersionRequest
{
    public string Currency { get; set; } = "COP";
    public decimal? MonthlyPrice { get; set; }
    public decimal? AnnualPrice { get; set; }
    public int? BranchLimit { get; set; }
    public int? UserLimit { get; set; }
    public IReadOnlyList<string> Modules { get; set; } = [];
}

public sealed record PlatformAuditDto(
    long Id,
    string Actor,
    string Action,
    string EntityType,
    string EntityId,
    string BeforeJson,
    string AfterJson,
    string IpAddress,
    string CorrelationId,
    DateTime CreatedAt);
