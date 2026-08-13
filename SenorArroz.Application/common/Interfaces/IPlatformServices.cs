using SenorArroz.Application.Features.Saas.DTOs;

namespace SenorArroz.Application.Common.Interfaces;

public interface IPlatformCurrentUser
{
    int Id { get; set; }
    string Name { get; set; }
    string Email { get; set; }
    bool IsAuthenticated { get; set; }
}

public interface IPlatformAuthService
{
    Task<PlatformLoginResult> LoginAsync(PlatformLoginRequest request, string? trustedDeviceToken, PlatformRequestContext context, CancellationToken cancellationToken = default);
    Task<PlatformLoginResult> VerifyOtpAsync(PlatformVerifyOtpRequest request, PlatformRequestContext context, CancellationToken cancellationToken = default);
    Task<PlatformSessionDto?> ValidateSessionAsync(string? sessionToken, string? csrfToken, bool requireCsrf, CancellationToken cancellationToken = default);
    Task LogoutAsync(string? sessionToken, PlatformRequestContext context, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlatformTrustedDeviceDto>> GetTrustedDevicesAsync(CancellationToken cancellationToken = default);
    Task RevokeTrustedDeviceAsync(Guid publicId, PlatformRequestContext context, CancellationToken cancellationToken = default);
}

public interface IPlatformService
{
    Task<IReadOnlyList<PlatformTenantListItemDto>> GetTenantsAsync(string? search, string? status, CancellationToken cancellationToken = default);
    Task<PlatformTenantDetailDto> GetTenantAsync(int id, CancellationToken cancellationToken = default);
    Task<PlatformTenantDetailDto> CreateTenantAsync(CreatePlatformTenantRequest request, PlatformRequestContext context, CancellationToken cancellationToken = default);
    Task<PlatformTenantDetailDto> UpdateTenantAsync(int id, UpdatePlatformTenantRequest request, PlatformRequestContext context, CancellationToken cancellationToken = default);
    Task<PlatformTenantDetailDto> ChangeTenantStatusAsync(int id, ChangeTenantStatusRequest request, PlatformRequestContext context, CancellationToken cancellationToken = default);
    Task<PlatformTenantDetailDto> ChangeSubscriptionAsync(int id, ChangeTenantSubscriptionRequest request, PlatformRequestContext context, CancellationToken cancellationToken = default);
    Task<PlatformTenantDetailDto> SetAddonAsync(int id, string addonCode, bool active, PlatformRequestContext context, CancellationToken cancellationToken = default);
    Task ResendInvitationAsync(int tenantId, PlatformRequestContext context, CancellationToken cancellationToken = default);
    Task AcceptInvitationAsync(AcceptTenantInvitationRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlatformPlanDto>> GetPlansAsync(CancellationToken cancellationToken = default);
    Task<PlatformPlanDto> CreatePlanAsync(CreatePlatformPlanRequest request, PlatformRequestContext context, CancellationToken cancellationToken = default);
    Task<PlatformPlanVersionDto> CreatePlanVersionAsync(int planId, UpsertPlanVersionRequest request, PlatformRequestContext context, CancellationToken cancellationToken = default);
    Task<PlatformPlanVersionDto> UpdatePlanVersionAsync(int versionId, UpsertPlanVersionRequest request, PlatformRequestContext context, CancellationToken cancellationToken = default);
    Task<PlatformPlanVersionDto> PublishPlanVersionAsync(int versionId, PlatformRequestContext context, CancellationToken cancellationToken = default);
    Task<PlatformPlanVersionDto> RetirePlanVersionAsync(int versionId, PlatformRequestContext context, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlatformCatalogItemDto>> GetModulesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlatformCatalogItemDto>> GetAddonsAsync(CancellationToken cancellationToken = default);
    Task<PlatformCatalogItemDto> UpsertModuleAsync(int? id, UpsertPlatformCatalogItemRequest request, PlatformRequestContext context, CancellationToken cancellationToken = default);
    Task<PlatformCatalogItemDto> UpsertAddonAsync(int? id, UpsertPlatformCatalogItemRequest request, PlatformRequestContext context, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> UpdateSettingsAsync(IReadOnlyDictionary<string, string> settings, PlatformRequestContext context, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlatformAuditDto>> GetAuditAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}
