using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Features.ExpenseHeaders.DTOs;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Tests;

public class ExpenseInvoiceTotalsHelperTests
{
    [Fact]
    public void IndividualVat_UsesOnlySelectedCreateLines()
    {
        var details = new[]
        {
            new CreateExpenseDetailDto { Quantity = 1, Amount = 10_000, Total = 10_000, IncludeVat = true },
            new CreateExpenseDetailDto { Quantity = 1, Amount = 5_000, Total = 5_000, IncludeVat = false },
        };

        var taxableSubtotal = ExpenseInvoiceTotalsHelper.TaxableSubtotalFromCreateDetails(
            details,
            includeVatForAll: false);

        Assert.Equal(10_000m, taxableSubtotal);
        Assert.Equal(1_900m, ExpenseInvoiceTotalsHelper.ComputeVatAmount(taxableSubtotal, true));
    }

    [Fact]
    public void GlobalVat_RemainsCompatibleAndIncludesEveryLine()
    {
        var details = new[]
        {
            new CreateExpenseDetailDto { Quantity = 1, Amount = 10_000, Total = 10_000 },
            new CreateExpenseDetailDto { Quantity = 1, Amount = 5_000, Total = 5_000 },
        };

        var taxableSubtotal = ExpenseInvoiceTotalsHelper.TaxableSubtotalFromCreateDetails(
            details,
            includeVatForAll: true);

        Assert.Equal(15_000m, taxableSubtotal);
        Assert.Equal(2_850m, ExpenseInvoiceTotalsHelper.ComputeVatAmount(taxableSubtotal, true));
    }

    [Fact]
    public void TrackedDetails_RecalculateVatFromPersistedLineSelection()
    {
        var details = new[]
        {
            new ExpenseDetail { Quantity = 2, Amount = 4_000, Total = 8_000, IncludeVat = false },
            new ExpenseDetail { Quantity = 3, Amount = 2_000, Total = 6_000, IncludeVat = true },
        };

        var taxableSubtotal = ExpenseInvoiceTotalsHelper.TaxableSubtotalFromTrackedDetails(details);

        Assert.Equal(6_000m, taxableSubtotal);
        Assert.Equal(1_140m, ExpenseInvoiceTotalsHelper.ComputeVatAmount(taxableSubtotal, true));
    }
}
