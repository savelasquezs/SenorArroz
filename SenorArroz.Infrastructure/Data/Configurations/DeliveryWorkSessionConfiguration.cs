using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class DeliveryWorkSessionConfiguration : IEntityTypeConfiguration<DeliveryWorkSession>
{
    public void Configure(EntityTypeBuilder<DeliveryWorkSession> builder)
    {
        builder.ToTable("delivery_work_session");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.DeliverymanId).HasColumnName("deliveryman_id").IsRequired();
        builder.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(x => x.DeviceInstallationId).HasColumnName("device_installation_id").HasMaxLength(64).IsRequired();
        builder.Property(x => x.DevicePlatform).HasColumnName("device_platform").HasMaxLength(30).IsRequired();
        builder.Property(x => x.DeviceDescription).HasColumnName("device_description").HasMaxLength(300);
        builder.Property(x => x.AppVersion).HasColumnName("app_version").HasMaxLength(40);
        builder.Property(x => x.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(x => x.AutoCloseAt).HasColumnName("auto_close_at").IsRequired();
        builder.Property(x => x.EndedAt).HasColumnName("ended_at");
        builder.Property(x => x.EndReason).HasColumnName("end_reason").HasMaxLength(40).HasConversion(
            value => value.HasValue ? ToSnakeCase(value.Value.ToString()) : null,
            value => value == null ? null : Enum.Parse<DeliveryWorkSessionEndReason>(ToPascalCase(value), true));
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).HasConversion(
            value => ToSnakeCase(value.ToString()),
            value => Enum.Parse<DeliveryWorkSessionStatus>(ToPascalCase(value), true)).IsRequired();
        builder.Property(x => x.LastCommunicationAt).HasColumnName("last_communication_at").IsRequired();
        builder.Property(x => x.StayAnalysisLastLocationId).HasColumnName("stay_analysis_last_location_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(x => x.Deliveryman).WithMany().HasForeignKey(x => x.DeliverymanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.DeliverymanId, x.Status })
            .HasFilter("status = 'active'")
            .IsUnique()
            .HasDatabaseName("uq_delivery_work_session_active_deliveryman");
        builder.HasIndex(x => new { x.BranchId, x.Status, x.AutoCloseAt })
            .HasDatabaseName("idx_delivery_work_session_branch_status_close");
    }

    private static string ToSnakeCase(string input)
    {
        var result = new StringBuilder();
        for (var i = 0; i < input.Length; i++)
        {
            if (i > 0 && char.IsUpper(input[i])) result.Append('_');
            result.Append(char.ToLowerInvariant(input[i]));
        }
        return result.ToString();
    }

    private static string ToPascalCase(string input) =>
        string.Concat(input.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
