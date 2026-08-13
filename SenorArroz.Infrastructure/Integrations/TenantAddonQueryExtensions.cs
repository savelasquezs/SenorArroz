using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities.Common;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Integrations;

internal static class TenantAddonQueryExtensions
{
    public static IQueryable<TEntity> WhereAddonEnabled<TEntity>(
        this IQueryable<TEntity> query,
        IApplicationDbContext db,
        string addonCode) where TEntity : BaseEntity =>
        query.Where(entity => entity.TenantId.HasValue
                              && db.Tenants.Any(tenant => tenant.Id == entity.TenantId && tenant.Status == TenantStatus.Active)
                              && db.TenantAddons.Any(assignment => assignment.TenantId == entity.TenantId
                                                                    && assignment.Active
                                                                    && assignment.Addon.Active
                                                                    && assignment.Addon.Code == addonCode));
}
