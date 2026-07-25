using Microsoft.AspNetCore.Http;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Infrastructure.Services;

public sealed class BranchContextService : IBranchContext
{
    public const string HeaderName = "X-Branch-Id";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICurrentUser _currentUser;

    public BranchContextService(
        IHttpContextAccessor httpContextAccessor,
        ICurrentUser currentUser)
    {
        _httpContextAccessor = httpContextAccessor;
        _currentUser = currentUser;
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

            return branchId;
        }
    }

    public bool HasExplicitSelection => SelectedBranchId.HasValue;

    public int? EffectiveBranchId =>
        IsSuperadmin
            ? SelectedBranchId
            : AssignedBranchId > 0
                ? AssignedBranchId
                : null;

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
            return AssignedBranchId;
        }

        if (selected.HasValue && requested.HasValue && selected.Value != requested.Value)
            throw new BranchScopeMismatchException();

        return selected ?? requested;
    }

    public void EnsureAccess(int resourceBranchId)
    {
        if (resourceBranchId <= 0)
            throw new BranchScopeMismatchException();

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
}
