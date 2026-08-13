using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Saas.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Services;

public sealed class TenantCapabilityService : ITenantCapabilityService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenant _currentTenant;

    public TenantCapabilityService(ApplicationDbContext context, ICurrentTenant currentTenant)
    {
        _context = context;
        _currentTenant = currentTenant;
    }

    public async Task<TenantContextDto> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (!_currentTenant.HasTenant) throw new UnauthorizedAccessException("No existe un tenant autenticado.");
        var tenant = await _context.Tenants.AsNoTracking().SingleAsync(x => x.Id == _currentTenant.TenantId, cancellationToken);
        var subscription = await _context.TenantSubscriptions.AsNoTracking()
            .Include(x => x.PlanVersion).ThenInclude(x => x.Plan)
            .Include(x => x.PlanVersion).ThenInclude(x => x.Modules).ThenInclude(x => x.Module)
            .SingleAsync(x => x.TenantId == tenant.Id && x.Status == TenantSubscriptionStatus.Active, cancellationToken);
        var addons = await _context.TenantAddons.AsNoTracking().Include(x => x.Addon)
            .Where(x => x.TenantId == tenant.Id && x.Active && x.Addon.Active).Select(x => x.Addon.Code).OrderBy(x => x).ToListAsync(cancellationToken);
        var branchCount = await _context.Branches.CountAsync(cancellationToken);
        var userCount = await _context.Users.CountAsync(cancellationToken);
        var month = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var usage = await _context.TenantUsageMonthly.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant.Id && x.Month == month, cancellationToken);
        return new TenantContextDto(
            tenant.Id, tenant.PublicId, tenant.DisplayName, tenant.Slug, tenant.Status.ToString().ToLowerInvariant(),
            subscription.PlanVersion.Plan.Code, subscription.PlanVersion.Plan.Name, subscription.PlanVersion.VersionNumber,
            subscription.PlanVersion.BranchLimit, subscription.PlanVersion.UserLimit, branchCount, userCount,
            subscription.PlanVersion.Modules.Where(x => x.Module.Active).Select(x => x.Module.Code).OrderBy(x => x).ToList(), addons,
            new TenantUsageDto(usage?.Orders ?? 0, usage?.StorageBytes ?? 0, usage?.AiInputTokens ?? 0, usage?.AiOutputTokens ?? 0, usage?.AiEstimatedCostUsd ?? 0));
    }

    public async Task<bool> HasModuleAsync(string moduleCode, CancellationToken cancellationToken = default) =>
        (await GetCurrentAsync(cancellationToken)).Modules.Contains(moduleCode, StringComparer.OrdinalIgnoreCase);

    public async Task<bool> HasAddonAsync(string addonCode, CancellationToken cancellationToken = default) =>
        (await GetCurrentAsync(cancellationToken)).Addons.Contains(addonCode, StringComparer.OrdinalIgnoreCase);

    public async Task EnsureCanCreateBranchAsync(CancellationToken cancellationToken = default)
    {
        var context = await GetCurrentAsync(cancellationToken);
        if (context.BranchLimit.HasValue && context.BranchCount >= context.BranchLimit.Value)
            throw new InvalidOperationException("Se alcanzó el límite de sucursales del plan.");
    }

    public async Task EnsureCanCreateUserAsync(CancellationToken cancellationToken = default)
    {
        var context = await GetCurrentAsync(cancellationToken);
        if (context.UserLimit.HasValue && context.UserCount >= context.UserLimit.Value)
            throw new InvalidOperationException("Se alcanzó el límite de usuarios del plan.");
    }
}
