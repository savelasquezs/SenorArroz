using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Tests;

public sealed class DataGatewayMcpControllerTests
{
    [Fact]
    public async Task Mpc_endpoint_is_closed_when_gateway_is_not_enabled()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataGateway:Enabled"] = "false"
        }).Build();
        var tenant = new Mock<ICurrentTenant>();
        tenant.SetupGet(x => x.HasTenant).Returns(true);
        var controller = new DataGatewayMcpController(
            config, tenant.Object, NullLogger<DataGatewayController>.Instance);
        using var document = JsonDocument.Parse(
            """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");
        var result = await controller.Handle(document.RootElement, CancellationToken.None);
        Assert.IsType<NotFoundResult>(result);
    }
}
