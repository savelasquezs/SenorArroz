using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Application.Features.CashRegister.Helpers;

internal static class CashRegisterExemptOrderIds
{
    /// <summary>Pedidos exentos del bloqueo de cuadre por préstamos informales activos.</summary>
    public static async Task<HashSet<int>> ActiveExemptOrderIdsAsync(
        IApplicationDbContext context,
        int branchId,
        CancellationToken cancellationToken = default)
    {
        var ids = await context.BranchInformalLoanExemptOrders
            .Where(e => e.Loan.BranchId == branchId && e.Loan.DeactivatedAt == null)
            .Select(e => e.OrderId)
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }
}
