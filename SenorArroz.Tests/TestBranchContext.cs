using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Tests;

internal sealed class TestBranchContext(int branchId = 1) : IBranchContext
{
    public int AssignedBranchId => branchId;
    public int? SelectedBranchId => branchId;
    public int? EffectiveBranchId => branchId;
    public bool HasExplicitSelection => true;

    public int RequireBranch(int? requestedBranchId = null)
    {
        if (requestedBranchId is > 0 && requestedBranchId != branchId)
            throw new BranchScopeMismatchException();
        return branchId;
    }

    public int? ResolveOptional(int? requestedBranchId = null) =>
        RequireBranch(requestedBranchId);

    public void EnsureAccess(int resourceBranchId)
    {
        if (resourceBranchId != branchId)
            throw new BranchScopeMismatchException();
    }
}
