using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.API.Security;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Tests;

public sealed class DataGatewaySecurityTests
{
    private static IConfiguration Config => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["DataGateway:Enabled"] = "true", ["DataGateway:ReaderTenantId"] = "1",
        ["DataGateway:AllowedViews:0"] = "diagnostics.orders_summary",
        ["DataGateway:OAuth:AllowedSubjects:0"] = "owner",
        ["ConnectionStrings:DataGatewayReader"] = "Host=127.0.0.1;Port=1;Database=test;Username=senorarroz_db_reader"
    }).Build();

    [Theory]
    [InlineData("DataGatewayOAuth", "owner", "gateway:read", "1", true)]
    [InlineData("DataGatewayOAuth", "other", "gateway:read", "1", false)]
    [InlineData("DataGatewayOAuth", "owner", "other", "1", false)]
    [InlineData("DataGatewayOAuth", "owner", "gateway:read", "2", false)]
    [InlineData("DataGatewayOAuth", "owner", "gateway:read", "", false)]
    public void OAuth_requires_subject_scope_and_pinned_tenant(string auth, string sub, string scope, string tenant, bool expected)
    {
        var identity = new ClaimsIdentity([new("sub", sub), new("scope", scope), new("tenant_id", tenant)], auth);
        Assert.Equal(expected, DataGatewaySecurity.Authorized(new ClaimsPrincipal(identity), Config));
    }

    [Theory]
    [InlineData("Cashier", "1", false)]
    [InlineData("Admin", "1", false)]
    [InlineData("Superadmin", "2", false)]
    [InlineData("Superadmin", "1", true)]
    public void Local_auth_requires_superadmin_for_pinned_tenant(string role, string tenant, bool expected)
    {
        var identity = new ClaimsIdentity([new(ClaimTypes.Role, role), new("tenant_id", tenant)], "Bearer");
        Assert.Equal(expected, DataGatewaySecurity.Authorized(new ClaimsPrincipal(identity), Config));
    }

    [Theory]
    [InlineData("{\"view\":\"diagnostics.orders_summary\",\"sql\":\"DELETE FROM x\"}")]
    [InlineData("{\"view\":\"diagnostics.orders_summary\",\"filters\":[{\"column\":\"id\",\"op\":\"eq\",\"value\":1,\"sql\":\"x\"}]}")]
    public void Rejects_unknown_query_properties(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DataGatewayController.QueryRequest>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));

    [Theory]
    [InlineData("{\"jsonrpc\":1,\"id\":1,\"method\":\"ping\"}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":{},\"method\":\"ping\"}")]
    [InlineData("[]")]
    public async Task Malformed_rpc_does_not_throw(string json)
    {
        var controller = Mcp();
        using var document = JsonDocument.Parse(json);
        Assert.IsAssignableFrom<IActionResult>(await controller.Handle(document.RootElement, default));
    }

    [Fact]
    public async Task Mcp_rejects_foreign_origin_and_unknown_protocol()
    {
        var controller = Mcp();
        using var doc = JsonDocument.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");
        controller.Request.Headers.Origin = "https://evil.example";
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await controller.Handle(doc.RootElement, default)).StatusCode);
        controller.Request.Headers.Remove("Origin");
        controller.Request.Headers["MCP-Protocol-Version"] = "invalid";
        Assert.IsType<BadRequestObjectResult>(await controller.Handle(doc.RootElement, default));
    }

    [Fact]
    public async Task Negotiates_protocol_preserves_large_id_and_annotations()
    {
        var controller = Mcp();
        using var doc = JsonDocument.Parse("""{"jsonrpc":"2.0","id":999999999999999999999999999,"method":"initialize","params":{"protocolVersion":"2025-11-25"}}""");
        var result = Assert.IsType<OkObjectResult>(await controller.Handle(doc.RootElement, default));
        var json = JsonSerializer.SerializeToElement(result.Value);
        Assert.Equal("2025-11-25", json.GetProperty("result").GetProperty("protocolVersion").GetString());
        Assert.Equal("999999999999999999999999999", json.GetProperty("id").GetRawText());
        using var list = JsonDocument.Parse("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        var tools = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await controller.Handle(list.RootElement, default)).Value);
        Assert.All(tools.GetProperty("result").GetProperty("tools").EnumerateArray(),
            t => Assert.True(t.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean()));
    }

    private static DataGatewayMcpController Mcp()
    {
        var tenant = new Mock<ICurrentTenant>();
        tenant.SetupGet(t => t.HasTenant).Returns(true);
        tenant.SetupGet(t => t.TenantId).Returns(1);
        return new(Config, tenant.Object, NullLogger<DataGatewayController>.Instance)
        { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
    }
}
