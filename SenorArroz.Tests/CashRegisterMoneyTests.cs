using SenorArroz.Application.Features.CashRegister.Helpers;

namespace SenorArroz.Tests;

public class CashRegisterMoneyTests
{
    [Theory]
    [InlineData(1000.49, 1000, true)]
    [InlineData(1000.50, 1001, true)]
    [InlineData(1000.49, 1001, false)]
    public void EqualInWholePesos_UsesDisplayedPesoPrecision(decimal actual, decimal expected, bool expectedEqual)
    {
        Assert.Equal(expectedEqual, CashRegisterMoney.EqualInWholePesos(actual, expected));
    }

    [Fact]
    public void DifferenceInWholePesos_IgnoresHiddenCentDifferenceThatDisplaysAsZero()
    {
        Assert.Equal(0, CashRegisterMoney.DifferenceInWholePesos(16642236m, 16642236.25m));
    }
}
