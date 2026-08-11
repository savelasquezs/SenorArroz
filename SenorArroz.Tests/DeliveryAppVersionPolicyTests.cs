using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SenorArroz.API.Infrastructure;
using SenorArroz.API.Middleware;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Exceptions;
using System.Security.Claims;

namespace SenorArroz.Tests;

public class DeliveryAppVersionPolicyTests
{
    private static readonly DeliveryAppClientVersion ValidClient = new(
        "1.2.5",
        11,
        DeliveryAppVersionOptions.RequiredPackageName);

    [Theory]
    [InlineData("1.2.5", 11, false)]
    [InlineData("1.2.5", 15, false)]
    [InlineData("1.2.4", 11, true)]
    [InlineData("1.2.6", 11, true)]
    [InlineData("1.2.5", 10, true)]
    public void Evaluate_EnforcesExactVersionAndMinimumBuild(
        string version,
        int build,
        bool updateRequired)
    {
        var result = CreatePolicy().Evaluate(new DeliveryAppClientVersion(
            version,
            build,
            DeliveryAppVersionOptions.RequiredPackageName));

        Assert.Equal(updateRequired, result.UpdateRequired);
    }

    [Fact]
    public void Evaluate_MissingHeaders_RequiresUpdate()
    {
        Assert.True(CreatePolicy().Evaluate(null).UpdateRequired);
    }

    [Fact]
    public void Evaluate_WrongPackage_RequiresUpdate()
    {
        var result = CreatePolicy().Evaluate(
            ValidClient with { PackageName = "com.example.delivery" });

        Assert.True(result.UpdateRequired);
    }

    [Fact]
    public async Task Middleware_DeliverymanWithMissingHeaders_ReturnsUpdateException()
    {
        var context = AuthenticatedContext("Deliveryman");
        var middleware = new DeliveryAppVersionMiddleware(_ => Task.CompletedTask);

        await Assert.ThrowsAsync<DeliveryAppUpdateRequiredException>(
            () => middleware.InvokeAsync(context, CreatePolicy()));
    }

    [Fact]
    public async Task Middleware_WebDeliveryman_DoesNotRequireFlutterHeaders()
    {
        var called = false;
        var context = AuthenticatedContext("Deliveryman");
        context.Request.Headers[DeliveryAppVersionHeaders.Client] =
            DeliveryAppVersionHeaders.WebClient;
        var middleware = new DeliveryAppVersionMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, CreatePolicy());

        Assert.True(called);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Superadmin")]
    [InlineData("Cashier")]
    [InlineData("Kitchen")]
    public async Task Middleware_OtherRoles_DoNotRequireFlutterHeaders(string role)
    {
        var called = false;
        var context = AuthenticatedContext(role);
        var middleware = new DeliveryAppVersionMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, CreatePolicy());

        Assert.True(called);
    }

    private static DeliveryAppVersionPolicy CreatePolicy()
    {
        return new DeliveryAppVersionPolicy(Options.Create(new DeliveryAppVersionOptions()));
    }

    private static DefaultHttpContext AuthenticatedContext(string role)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, role)],
            "test"));
        return context;
    }
}
