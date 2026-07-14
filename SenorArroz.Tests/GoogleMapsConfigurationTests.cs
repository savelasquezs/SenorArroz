using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Options;
using SenorArroz.Infrastructure;

namespace SenorArroz.Tests;

public class GoogleMapsConfigurationTests
{
    [Fact]
    public void EnvironmentStyleSecretsOverrideGoogleMapsSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GoogleMaps:GeocodingApiKey"] = "section-geocoding",
                ["GoogleMaps:RoutesApiKey"] = "section-routes",
                ["GOOGLE_MAPS_GEOCODING_API_KEY"] = "environment-geocoding",
                ["GOOGLE_MAPS_ROUTES_API_KEY"] = "environment-routes"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddInfrastructureServices(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<GoogleMapsRouteOptions>>().Value;
        Assert.Equal("environment-geocoding", options.GeocodingApiKey);
        Assert.Equal("environment-routes", options.RoutesApiKey);
    }
}
