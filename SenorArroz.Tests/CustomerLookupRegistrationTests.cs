using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SenorArroz.Application.Features.Customers.Queries;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

public sealed class CustomerLookupRegistrationTests
{
    [Fact]
    public void Provider_specific_handler_is_registered_without_a_provider_dependency_in_application()
    {
        var services = new ServiceCollection();
        SenorArroz.Infrastructure.DependencyInjection.AddInfrastructureServices(services, new ConfigurationBuilder().Build());
        var registration = Assert.Single(services.Where(s => s.ServiceType == typeof(IRequestHandler<GetCustomerSuggestionsQuery, CustomerSuggestionPageDto>)));
        Assert.Equal(typeof(GetCustomerSuggestionsHandler), registration.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
    }
}
