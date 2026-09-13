using System.Reflection;
using System.Text.Json;
using SenorArroz.API.Services;

namespace SenorArroz.Tests;

public class WhatsAppFulfillmentScreenTests
{
    [Theory]
    [InlineData("delivery", "ADDRESS_DELIVERY")]
    [InlineData("pickup", "ADDRESS_PICKUP")]
    public void AddressScreen_UsesDistinctScreenForEachFulfillment(string fulfillmentType, string expected)
    {
        var method = typeof(WhatsAppCommerceFlowService).GetMethod(
            "AddressScreen",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var state = new WhatsAppCommerceState { FulfillmentType = fulfillmentType };

        Assert.Equal(expected, method!.Invoke(null, [state]));
    }

    [Theory]
    [InlineData("ADDRESS_DELIVERY")]
    [InlineData("ADDRESS_PICKUP")]
    public void BackFromFulfillmentDetail_ReturnsToFulfillment(string currentScreen)
    {
        var method = typeof(WhatsAppCommerceFlowService).GetMethod(
            "ResolveBackScreen",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var state = new WhatsAppCommerceState
        {
            FulfillmentType = currentScreen == "ADDRESS_PICKUP" ? "pickup" : "delivery",
            LastScreen = currentScreen
        };

        Assert.Equal("FULFILLMENT", method!.Invoke(null, [currentScreen, state]));
    }

    [Fact]
    public void FlowDefinition_HasSeparateDeliveryAndPickupScreens()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "WhatsAppFlows", "storefront-flow.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        var screenIds = root.GetProperty("screens")
            .EnumerateArray()
            .Select(screen => screen.GetProperty("id").GetString())
            .ToArray();

        Assert.Contains("ADDRESS_DELIVERY", screenIds);
        Assert.Contains("ADDRESS_PICKUP", screenIds);

        var fulfillmentRoutes = root.GetProperty("routing_model")
            .GetProperty("FULFILLMENT")
            .EnumerateArray()
            .Select(route => route.GetString())
            .ToArray();

        Assert.Contains("ADDRESS_DELIVERY", fulfillmentRoutes);
        Assert.Contains("ADDRESS_PICKUP", fulfillmentRoutes);
    }
}
