using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SenorArroz.API.Security;
using SenorArroz.Application.Options;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public sealed class StorefrontApiKeyAuthenticationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public async Task ValidKey_ProvidesTenantContextForCatalogQueries(int tenantId)
    {
        using var provider = CreateServices(tenantId);
        var context = CreateHttpContext(provider);

        var result = await context.AuthenticateAsync(StorefrontApiKeyOptions.Scheme);

        Assert.True(result.Succeeded);
        Assert.Equal(tenantId.ToString(), result.Principal?.FindFirst("tenant_id")?.Value);

        // Authorization assigns the authenticated principal before the controller queries EF.
        context.User = result.Principal!;
        var tenant = new CurrentTenantService(
            provider.GetRequiredService<IHttpContextAccessor>(),
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Multitenancy:DefaultTenantId"] = "999" }).Build());

        // Authentication must use the configured storefront tenant, not an anonymous fallback.
        Assert.Equal(tenantId, tenant.TenantId);
    }

    [Fact]
    public async Task MissingStorefrontTenant_RejectsRequestInsteadOfReturningEmptyCatalog()
    {
        using var provider = CreateServices(0);
        var result = await CreateHttpContext(provider)
            .AuthenticateAsync(StorefrontApiKeyOptions.Scheme);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task InvalidKey_CannotAcquireTenantContext()
    {
        using var provider = CreateServices(1);
        var context = CreateHttpContext(provider);
        context.Request.Headers["X-Storefront-Key"] = "invalid";

        var result = await context.AuthenticateAsync(StorefrontApiKeyOptions.Scheme);

        Assert.False(result.Succeeded);
        Assert.Null(result.Principal);
    }

    private static DefaultHttpContext CreateHttpContext(IServiceProvider provider)
    {
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Headers["X-Storefront-Key-Id"] = "web-main";
        context.Request.Headers["X-Storefront-Key"] = "storefront-secret";
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        return context;
    }

    private static ServiceProvider CreateServices(int tenantId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.Configure<StorefrontApiKeyOptions>(options =>
        {
            options.KeyId = "web-main";
            options.KeyHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes("storefront-secret")));
        });
        services.Configure<StorefrontCustomerAuthOptions>(options => options.TenantId = tenantId);
        services.AddSingleton<StorefrontApiKeyValidator>();
        services.AddAuthentication()
            .AddScheme<StorefrontApiKeyOptions, StorefrontApiKeyAuthenticationHandler>(
                StorefrontApiKeyOptions.Scheme, _ => { });
        return services.BuildServiceProvider();
    }
}
