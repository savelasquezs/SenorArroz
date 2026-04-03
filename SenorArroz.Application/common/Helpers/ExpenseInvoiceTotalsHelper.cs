using SenorArroz.Application.Features.ExpenseHeaders.DTOs;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Helpers;

public static class ExpenseInvoiceTotalsHelper
{
    public const decimal DefaultVatRate = 0.19m;

    /// <summary>
    /// Total de línea persistido: prioriza el monto explícito del cliente (factura);
    /// solo si no se envía se deriva de cantidad × valor unitario.
    /// </summary>
    public static decimal ResolveLineTotal(decimal quantity, int amount, decimal? lineTotal) =>
        lineTotal.HasValue
            ? Math.Round(lineTotal.Value, 2, MidpointRounding.AwayFromZero)
            : Math.Round(quantity * amount, 2, MidpointRounding.AwayFromZero);

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
