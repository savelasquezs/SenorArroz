using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Services;

public static class TenantAccessRules
{
    public static bool CanAuthenticate(User user) =>
        user.TenantId > 0
        && user.Tenant is not null
        && user.Tenant.Id == user.TenantId
        && user.Tenant.IsActive
        && user.Tenant.Status == TenantStatus.Active
        && user.Branch is not null
        && user.Branch.TenantId == user.TenantId;
}
