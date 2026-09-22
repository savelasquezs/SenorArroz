using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Infrastructure.Services;

public sealed class BranchContextService : IBranchContext
{
    public const string HeaderName = "X-Branch-Id";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICurrentUser _currentUser;
    private readonly IApplicationDbContext? _db;
    private readonly ICurrentTenant? _currentTenant;

    public BranchContextService(
        IHttpContextAccessor httpContextAccessor,
        ICurrentUser currentUser,
        IApplicationDbContext? db = null,
        ICurrentTenant? currentTenant = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _currentUser = currentUser;
        _db = db;
        _currentTenant = currentTenant;
    }

    public int AssignedBranchId => _currentUser.BranchId;

    public int? SelectedBranchId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.Request.Headers[HeaderName].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (!int.TryParse(value, out var branchId) || branchId <= 0)
                throw new BranchContextRequiredException();

            if (!IsSuperadmin && branchId != AssignedBranchId)
                throw new BranchAccessDeniedException();

            EnsureBranchBelongsToTenant(branchId);

            return branchId;
        }
    }

    public bool HasExplicitSelection => SelectedBranchId.HasValue;

    public int? EffectiveBranchId
    {
        get
        {
            var branchId = IsSuperadmin
                ? SelectedBranchId
                : AssignedBranchId > 0
                    ? AssignedBranchId
                    : null;
            if (branchId.HasValue)
                EnsureBranchBelongsToTenant(branchId.Value);
            return branchId;
        }
    }

    public int RequireBranch(int? requestedBranchId = null) =>
        ResolveOptional(requestedBranchId) ?? throw new BranchContextRequiredException();

    public int? ResolveOptional(int? requestedBranchId = null)
    {
        var requested = requestedBranchId is > 0 ? requestedBranchId : null;
        var selected = SelectedBranchId;

        if (!IsSuperadmin)
        {
            if (AssignedBranchId <= 0)
                throw new BranchContextRequiredException();
            if (requested.HasValue && requested.Value != AssignedBranchId)
                throw new BranchAccessDeniedException();
            EnsureBranchBelongsToTenant(AssignedBranchId);
            return AssignedBranchId;
        }

        if (selected.HasValue && requested.HasValue && selected.Value != requested.Value)
            throw new BranchScopeMismatchException();

        if (requested.HasValue)
            EnsureBranchBelongsToTenant(requested.Value);

        return selected ?? requested;
    }

    public void EnsureAccess(int resourceBranchId)
    {
        if (resourceBranchId <= 0)
            throw new BranchScopeMismatchException();

        EnsureBranchBelongsToTenant(resourceBranchId);

        if (!IsSuperadmin)
        {
            if (resourceBranchId != AssignedBranchId)
                throw new BranchAccessDeniedException();
            return;
        }

        if (SelectedBranchId is int selected && resourceBranchId != selected)
            throw new BranchScopeMismatchException();
    }

    private bool IsSuperadmin =>
        string.Equals(_currentUser.Role, "superadmin", StringComparison.OrdinalIgnoreCase);

    private void EnsureBranchBelongsToTenant(int branchId)
    {
        if (!_currentUser.IsAuthenticated || _db is null || _currentTenant is null)
            return;
        if (!_currentTenant.HasTenant)
            throw new BranchAccessDeniedException();

        var context = _httpContextAccessor.HttpContext;
        var cacheKey = $"branch-tenant:{_currentTenant.TenantId}:{branchId}";
        if (context?.Items.TryGetValue(cacheKey, out var cached) == true && cached is true)
            return;

        var belongs = _db.Branches.AsNoTracking().Any(
            branch => branch.Id == branchId && branch.TenantId == _currentTenant.TenantId);
        if (!belongs)
            throw new BranchAccessDeniedException();

        if (context is not null)
            context.Items[cacheKey] = true;
    }
}
