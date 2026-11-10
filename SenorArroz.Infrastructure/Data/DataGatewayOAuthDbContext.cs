using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;
using System.Text.RegularExpressions;

namespace SenorArroz.Infrastructure.Data;

public sealed class DataGatewayOAuthDbContext(DbContextOptions<DataGatewayOAuthDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.UseOpenIddict();
        model.Entity<OpenIddictEntityFrameworkCoreApplication>().ToTable("application", "gateway_oauth");
        model.Entity<OpenIddictEntityFrameworkCoreAuthorization>().ToTable("authorization", "gateway_oauth");
        model.Entity<OpenIddictEntityFrameworkCoreScope>().ToTable("scope", "gateway_oauth");
        model.Entity<OpenIddictEntityFrameworkCoreToken>().ToTable("token", "gateway_oauth");
        foreach (var entity in model.Model.GetEntityTypes().ToArray())
            model.Entity(entity.ClrType).Property<int>("TenantId").HasDefaultValue(1);
        foreach (var entity in model.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
    }
}
