using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Tests;

public sealed class DataGatewayControllerTests
{
    private static DataGatewayController Create(bool enabled, bool validTenant = true)
    {
        var settings = new Dictionary<string, string?>
        {
            ["DataGateway:Enabled"] = enabled.ToString(),
            ["ConnectionStrings:DataGatewayReader"] = "Host=127.0.0.1;Database=test;Username=reader;Password=unused",
            ["DataGateway:AllowedViews:0"] = "diagnostics.orders_summary"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var tenant = new Mock<ICurrentTenant>();
        tenant.SetupGet(t => t.HasTenant).Returns(validTenant);
        tenant.SetupGet(t => t.TenantId).Returns(validTenant ? 1 : 0);
        return new DataGatewayController(config, tenant.Object, NullLogger<DataGatewayController>.Instance);
    }

    [Fact]
    public async Task Disabled_gateway_returns_404_without_connecting()
    {
        var controller = Create(false);
        var result = await controller.Schema(CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Missing_tenant_returns_404_without_connecting()
    {
        var controller = Create(true, false);
        var result = await controller.Query(
            new DataGatewayController.QueryRequest("diagnostics.orders_summary"),
            CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData("public.orders")]
    [InlineData("diagnostics.orders_summary;DROP TABLE orders")]
    [InlineData("diagnostics.not_allowlisted")]
    public async Task Rejects_non_allowlisted_view_before_database_access(string view)
    {
        var controller = Create(true);
        var result = await controller.Query(
            new DataGatewayController.QueryRequest(view),
            CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }
}
