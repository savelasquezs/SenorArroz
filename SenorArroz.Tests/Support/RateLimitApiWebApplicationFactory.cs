using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests.Support;

/// <summary>
/// Host real de la API con EF InMemory y límites de rate bajos para pruebas rápidas.
/// </summary>
public sealed class RateLimitApiWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Carga appsettings.Test.json (límites bajos) vía Program.cs + WebApplicationFactory.
        builder.UseEnvironment("Test");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<ApplicationDbContext>));
            services.RemoveAll(typeof(ApplicationDbContext));
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase("RateLimitTests_" + Guid.NewGuid().ToString("N")));
        });
    }
}
