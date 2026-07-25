namespace SenorArroz.Application.Common.Interfaces;

/// <summary>
/// Resolves the operational branch for the current HTTP request.
/// The authenticated user's assigned branch remains available through
/// <see cref="ICurrentUser.BranchId"/>; this context adds the branch selected
/// by a Superadmin without changing JWT identity.
/// </summary>
public interface IBranchContext
{
    int AssignedBranchId { get; }
    int? SelectedBranchId { get; }
    int? EffectiveBranchId { get; }
    bool HasExplicitSelection { get; }

    int RequireBranch(int? requestedBranchId = null);
    int? ResolveOptional(int? requestedBranchId = null);
    void EnsureAccess(int resourceBranchId);
}
