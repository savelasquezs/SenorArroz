using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using SenorArroz.Application;

namespace SenorArroz.Tests;

public class AutoMapperConfigurationTests
{
    [Fact]
    public void ApplicationMappings_AreValid()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();

        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();

        mapper.ConfigurationProvider.AssertConfigurationIsValid();
    }
}
