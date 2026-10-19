using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Common.Helpers;

public static class OrderAccessRules
{
    public static bool IsManagementRole(string role) => Roles.IsSuperadminOrAdminOrCashier(role);

    public static bool IsOriginAccess(Order order, ICurrentUser user, IBranchContext? branch = null)
    {
        var workspace = branch?.EffectiveBranchId ?? user.BranchId;
        return IsManagementRole(user.Role) && user.TenantId > 0 && order.TenantId == user.TenantId
            && workspace > 0 && order.OriginBranchId == workspace && order.BranchId != workspace;
    }

    public static void EnsureAccess(Order order, ICurrentUser user, IBranchContext? branch = null)
    {
        if (user.TenantId > 0 && order.TenantId != user.TenantId)
            throw new BranchAccessDeniedException();
        if (IsOriginAccess(order, user, branch))
        {
            branch?.EnsureAccess(order.OriginBranchId!.Value);
            return;
        }
        // Legacy callers without a branch context retain their existing local-role semantics.
        if (branch is not null) branch.EnsureAccess(order.BranchId);
        else if (!Roles.IsSuperadmin(user.Role) && order.BranchId != user.BranchId)
            throw new BranchAccessDeniedException();
    }

    // Controller postconditions operate on a DTO already authorized and tenant-filtered by the handler.
    public static void EnsureReturnedScope(int destination, int? origin, ICurrentUser user, IBranchContext branch)
    {
        var workspace = branch.EffectiveBranchId;
        branch.EnsureAccess(IsManagementRole(user.Role) && workspace > 0 && origin == workspace
            ? origin.Value : destination);
    }

    // Existing payments may belong to a former dispatch branch after a Superadmin
    // transfer. Only Superadmin may verify/unverify such bank receipts; the bank
    // must remain in the authenticated tenant. Other payment mutations still use
    // the strict two-argument branch check.
    public static void EnsureExistingBankPaymentBranch(
        Order order, int bankBranchId, int bankTenantId, ICurrentUser actor)
    {
        if (bankBranchId == order.BranchId) return;
        if (Roles.IsSuperadmin(actor.Role) && actor.TenantId > 0
            && actor.TenantId == order.TenantId && bankTenantId == actor.TenantId) return;
        EnsurePaymentBranch(order, bankBranchId);
    }

    public static void EnsurePaymentBranch(Order order, int bankBranchId)
    {
        if (bankBranchId != order.BranchId)
            throw new BusinessException("El pago y el banco deben pertenecer a la sucursal que despacha el pedido.");
    }
}
