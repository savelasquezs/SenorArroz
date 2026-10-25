using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.API.Controllers;

namespace SenorArroz.Tests;

public sealed class CustomerLookupRouteTests
{
    [Fact]
    public void Lookup_routes_are_authorized_uncached_and_cannot_bind_legacy_customer_id()
    {
        var controller = typeof(CustomerLookupController);
        Assert.NotNull(controller.GetCustomAttribute<AuthorizeAttribute>());
        var cache = controller.GetCustomAttribute<ResponseCacheAttribute>();
        Assert.True(cache!.NoStore);
        Assert.Equal(ResponseCacheLocation.None, cache.Location);
        Assert.Equal("api/customers/lookup", controller.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("search", controller.GetMethod(nameof(CustomerLookupController.Search))!
            .GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{id:int}", controller.GetMethod(nameof(CustomerLookupController.Select))!
            .GetCustomAttribute<HttpGetAttribute>()!.Template);
    }
}
