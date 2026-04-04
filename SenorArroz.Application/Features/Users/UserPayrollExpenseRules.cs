using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.Users;

internal static class UserPayrollExpenseRules
{
    public static async Task ValidatePayrollExpenseAssignmentAsync(
        int? payrollExpenseId,
        int? excludeUserId,
        IApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        if (!payrollExpenseId.HasValue)
            return;

        var exists = await db.Expenses.AnyAsync(e => e.Id == payrollExpenseId.Value, cancellationToken);
        if (!exists)
            throw new BusinessException("El gasto de nómina no existe en el catálogo.");

        var q = db.Users.Where(u => u.PayrollExpenseId == payrollExpenseId);
        if (excludeUserId.HasValue)
            q = q.Where(u => u.Id != excludeUserId.Value);

        if (await q.AnyAsync(cancellationToken))
            throw new BusinessException("Ese ítem de gasto ya está asignado a otro usuario.");
    }
}
