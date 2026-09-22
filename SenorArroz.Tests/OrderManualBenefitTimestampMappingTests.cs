using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed class OrderManualBenefitTimestampMappingTests
{
    [Fact]
    public void Manual_benefit_timestamp_is_mapped_as_utc_timestamptz()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=mapping_test").Options,
            currentTenant: TestTenantContext.Default,
            tenantExecutionContext: TestTenantContext.Default);

        var property = db.Model.FindEntityType(typeof(Order))!
            .FindProperty(nameof(Order.ManualBenefitGrantedAt))!;
        var converter = property.GetValueConverter()!;
        var unspecified = new DateTime(2026, 9, 19, 18, 22, 50, DateTimeKind.Unspecified);

        var providerValue = Assert.IsType<DateTime>(converter.ConvertToProvider(unspecified));

        Assert.Equal("timestamp with time zone", property.GetColumnType());
        Assert.Equal(DateTimeKind.Utc, providerValue.Kind);
        Assert.Equal(unspecified.Ticks, providerValue.Ticks);
    }
}
