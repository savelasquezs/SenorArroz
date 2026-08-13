using SenorArroz.Application.Features.Saas.DTOs;

namespace SenorArroz.Application.Common.Interfaces;

public interface ITenantCapabilityService
{
    Task<TenantContextDto> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task<bool> HasModuleAsync(string code, CancellationToken cancellationToken = default);
    Task<bool> HasAddonAsync(string code, CancellationToken cancellationToken = default);
    Task EnsureCanCreateBranchAsync(CancellationToken cancellationToken = default);
    Task EnsureCanCreateUserAsync(CancellationToken cancellationToken = default);
}
