using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Common;

/// <summary>
/// Prepara el agregado ExpenseHeader antes de DbSet.Update cuando se cargo con Include
/// y AsNoTracking. Evita adjuntar entidades de lookup/usuario duplicadas al DbContext.
/// </summary>
public static class ExpenseHeaderUpdateGraphForPersistence
{
    public static void DetachReadOnlyNavigations(ExpenseHeader expenseHeader)
    {
        expenseHeader.Branch = null!;
        expenseHeader.Supplier = null!;
        expenseHeader.CreatedBy = null!;
        expenseHeader.Deliveryman = null;

        foreach (var detail in expenseHeader.ExpenseDetails)
        {
            detail.Header = null!;
            detail.Expense = null!;
        }

        foreach (var payment in expenseHeader.ExpenseBankPayments)
        {
            payment.ExpenseHeader = null!;
            payment.Bank = null!;
        }
    }
}
