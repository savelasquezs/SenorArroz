using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Application.Features.CashRegister.Helpers;

namespace SenorArroz.Tests;

public class CashRegisterUnsettledAppsHelperTests
{
    [Fact]
    public void SumSnapshot_EmptyOrNull_ReturnsZero()
    {
        Assert.Equal(0, CashRegisterUnsettledAppsHelper.SumSnapshot(null));
        Assert.Equal(0, CashRegisterUnsettledAppsHelper.SumSnapshot(""));
        Assert.Equal(0, CashRegisterUnsettledAppsHelper.SumSnapshot("{}"));
    }

    [Fact]
    public void SumSnapshot_ValidJson_ReturnsSum()
    {
        var json = """[{"appId":1,"appName":"Rappi","amount":10000},{"appId":2,"appName":"Otro","amount":2500.50}]""";
        Assert.Equal(12500.50m, CashRegisterUnsettledAppsHelper.SumSnapshot(json));
    }

    [Fact]
    public void SerializeSnapshot_RoundTrip_MatchesSum()
    {
        var lines = new List<UnsettledAppLineDto>
        {
            new() { AppId = 1, AppName = "A", Amount = 100 },
            new() { AppId = 2, AppName = "B", Amount = 200 },
        };
        var json = CashRegisterUnsettledAppsHelper.SerializeSnapshot(lines);
        Assert.Equal(300m, CashRegisterUnsettledAppsHelper.SumSnapshot(json));
    }
}
