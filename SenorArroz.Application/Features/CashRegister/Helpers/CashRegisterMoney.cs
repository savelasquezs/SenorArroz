namespace SenorArroz.Application.Features.CashRegister.Helpers;

public static class CashRegisterMoney
{
    public static decimal ToWholePeso(decimal value) =>
        decimal.Round(value, 0, MidpointRounding.AwayFromZero);

    public static decimal DifferenceInWholePesos(decimal actual, decimal expected) =>
        ToWholePeso(actual) - ToWholePeso(expected);

    public static bool EqualInWholePesos(decimal actual, decimal expected) =>
        DifferenceInWholePesos(actual, expected) == 0;
}
