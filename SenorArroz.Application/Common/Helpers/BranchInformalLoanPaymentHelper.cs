using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Common.Helpers;

public static class BranchInformalLoanPaymentHelper
{
    public static BranchInformalLoanPayment Apply(
        IApplicationDbContext context,
        ICurrentUser currentUser,
        IClock clock,
        BranchInformalLoan loan,
        decimal amount,
        BranchInformalLoanPaymentKind kind,
        string? notes = null,
        int? expenseHeaderId = null)
    {
        if (loan.DeactivatedAt is not null)
            throw new BusinessException("El préstamo ya está inactivo");
        if (loan.Amount <= 0)
            throw new BusinessException("Este préstamo no admite abonos");
        if (amount <= 0)
            throw new BusinessException("El abono debe ser mayor que cero");
        if (amount > loan.Amount)
            throw new BusinessException("El abono no puede superar el saldo pendiente");
        if (kind == BranchInformalLoanPaymentKind.Expense && expenseHeaderId is null)
            throw new BusinessException("El abono por gasto requiere un gasto vinculado");
        if (kind == BranchInformalLoanPaymentKind.Cash && expenseHeaderId is not null)
            throw new BusinessException("El abono en efectivo no puede vincular un gasto");

        var now = clock.UtcNow;
        var payment = new BranchInformalLoanPayment
        {
            LoanId = loan.Id,
            Kind = kind,
            Amount = amount,
            BalanceBefore = loan.Amount,
            BalanceAfter = loan.Amount - amount,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedById = currentUser.Id,
            ExpenseHeaderId = expenseHeaderId,
            CreatedAt = now,
            UpdatedAt = now
        };

        loan.Amount = payment.BalanceAfter;
        if (loan.Amount == 0)
        {
            loan.DeactivatedAt = now;
            loan.DeactivatedById = currentUser.Id;
            loan.DeactivationNotes = "Pagado por completo";
        }

        context.BranchInformalLoanPayments.Add(payment);
        return payment;
    }
}
