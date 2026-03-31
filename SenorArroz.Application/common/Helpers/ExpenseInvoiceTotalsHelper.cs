using SenorArroz.Application.Features.ExpenseHeaders.DTOs;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Helpers;

public static class ExpenseInvoiceTotalsHelper
{
    public const decimal DefaultVatRate = 0.19m;

    public static decimal SubtotalFromCreateDetails(IEnumerable<CreateExpenseDetailDto> details) =>
        details.Sum(ed =>
            ed.Total ?? Math.Round(ed.Quantity * ed.Amount, 2, MidpointRounding.AwayFromZero));

    public static decimal SubtotalFromUpdateDetails(IEnumerable<UpdateExpenseDetailDto> details) =>
        details.Sum(ed =>
            ed.Total ?? Math.Round(ed.Quantity * ed.Amount, 2, MidpointRounding.AwayFromZero));

    public static decimal LineSubtotal(ExpenseDetail d) =>
        d.Total ?? Math.Round(d.Quantity * d.Amount, 2, MidpointRounding.AwayFromZero);

    public static decimal SubtotalFromTrackedDetails(IEnumerable<ExpenseDetail> details) =>
        details.Sum(LineSubtotal);

    public static decimal ComputeVatAmount(decimal subtotal, bool includeVat) =>
        includeVat ? Math.Round(subtotal * DefaultVatRate, 0, MidpointRounding.AwayFromZero) : 0m;

    public static decimal GrossTotal(decimal subtotal, decimal vatAmount) => subtotal + vatAmount;
}
