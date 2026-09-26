using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public sealed class StorefrontAnalyticsDailyConfiguration : IEntityTypeConfiguration<StorefrontAnalyticsDaily>
{
    public void Configure(EntityTypeBuilder<StorefrontAnalyticsDaily> builder)
    {
        builder.ToTable("storefront_analytics_daily");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.EventDate).HasColumnName("event_date").HasColumnType("date");
        builder.Property(x => x.EventName).HasColumnName("event_name").HasMaxLength(64);
        builder.Property(x => x.Path).HasColumnName("path").HasMaxLength(160);
        builder.Property(x => x.UtmSource).HasColumnName("utm_source").HasMaxLength(80);
        builder.Property(x => x.UtmMedium).HasColumnName("utm_medium").HasMaxLength(80);
        builder.Property(x => x.UtmCampaign).HasColumnName("utm_campaign").HasMaxLength(120);
        builder.Property(x => x.UtmContent).HasColumnName("utm_content").HasMaxLength(120);
        builder.Property(x => x.UtmTerm).HasColumnName("utm_term").HasMaxLength(120);
        builder.Property(x => x.BranchId).HasColumnName("branch_id");
        builder.Property(x => x.FulfillmentType).HasColumnName("fulfillment_type").HasMaxLength(24);
        builder.Property(x => x.PaymentType).HasColumnName("payment_type").HasMaxLength(24);
        builder.Property(x => x.StatusCode).HasColumnName("status_code");
        builder.Property(x => x.CoverageState).HasColumnName("coverage_state");
        builder.Property(x => x.EventCount).HasColumnName("event_count");
        builder.Property(x => x.ValueSum).HasColumnName("value_sum").HasPrecision(18, 2);
        WompiPaymentIntegrationConfiguration.Timestamps(builder);
        builder.HasIndex(x => new
        {
            x.TenantId,
            x.EventDate,
            x.EventName,
            x.Path,
            x.UtmSource,
            x.UtmMedium,
            x.UtmCampaign,
            x.UtmContent,
            x.UtmTerm,
            x.BranchId,
            x.FulfillmentType,
            x.PaymentType,
            x.StatusCode,
            x.CoverageState,
        }).IsUnique().HasDatabaseName("ux_storefront_analytics_daily_dimensions");
        builder.HasIndex(x => new { x.TenantId, x.EventDate, x.EventName })
            .HasDatabaseName("ix_storefront_analytics_daily_event");
    }
}
