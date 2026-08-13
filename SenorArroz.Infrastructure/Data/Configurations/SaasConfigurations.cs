using System.Text;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Data.Configurations;

public static class SaasConfigurations
{
    private static readonly Type[] ControlPlaneTypes =
    [
        typeof(Tenant), typeof(PlatformUser), typeof(PlatformSession), typeof(PlatformOtpChallenge),
        typeof(PlatformTrustedDevice), typeof(PlatformSetting), typeof(SaasModule), typeof(SaasAddon),
        typeof(SaasPlan), typeof(SaasPlanVersion), typeof(SaasPlanVersionModule), typeof(TenantSubscription),
        typeof(TenantAddon), typeof(TenantInvitation), typeof(PlatformAuditLog), typeof(TenantUsageMonthly)
    ];

    public static IReadOnlySet<Type> ControlPlaneEntityTypes { get; } = ControlPlaneTypes.ToHashSet();

    public static void ConfigureSaas(this ModelBuilder modelBuilder)
    {
        foreach (var type in ControlPlaneTypes)
            modelBuilder.Entity(type);

        modelBuilder.Entity<Tenant>().HasIndex(x => x.PublicId).IsUnique();
        modelBuilder.Entity<Tenant>().HasIndex(x => x.Slug).IsUnique();
        modelBuilder.Entity<Tenant>().Property(x => x.Status).HasConversion<string>();

        modelBuilder.Entity<PlatformUser>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<PlatformSession>().HasIndex(x => x.TokenHash).IsUnique();
        modelBuilder.Entity<PlatformOtpChallenge>().HasIndex(x => x.PublicId).IsUnique();
        modelBuilder.Entity<PlatformTrustedDevice>().HasIndex(x => x.PublicId).IsUnique();
        modelBuilder.Entity<PlatformTrustedDevice>().HasIndex(x => x.TokenHash).IsUnique();
        modelBuilder.Entity<PlatformSetting>().HasIndex(x => x.Key).IsUnique();
        modelBuilder.Entity<PlatformSetting>().Property(x => x.ValueJson).HasColumnType("jsonb");

        modelBuilder.Entity<SaasModule>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<SaasAddon>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<SaasPlan>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<SaasPlanVersion>().HasIndex(x => new { x.PlanId, x.VersionNumber }).IsUnique();
        modelBuilder.Entity<SaasPlanVersion>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<SaasPlanVersion>().Property(x => x.MonthlyPrice).HasPrecision(18, 2);
        modelBuilder.Entity<SaasPlanVersion>().Property(x => x.AnnualPrice).HasPrecision(18, 2);
        modelBuilder.Entity<SaasPlanVersionModule>().HasKey(x => new { x.PlanVersionId, x.ModuleId });

        modelBuilder.Entity<TenantSubscription>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<TenantSubscription>().HasIndex(x => x.TenantId).IsUnique().HasFilter("status = 'Active'");
        modelBuilder.Entity<TenantAddon>().HasIndex(x => new { x.TenantId, x.AddonId }).IsUnique();
        modelBuilder.Entity<TenantInvitation>().HasIndex(x => x.PublicId).IsUnique();
        modelBuilder.Entity<PlatformAuditLog>().Property(x => x.BeforeJson).HasColumnType("jsonb");
        modelBuilder.Entity<PlatformAuditLog>().Property(x => x.AfterJson).HasColumnType("jsonb");
        modelBuilder.Entity<PlatformAuditLog>().HasOne(x => x.PlatformUser).WithMany().HasForeignKey(x => x.PlatformUserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<TenantUsageMonthly>().HasIndex(x => new { x.TenantId, x.Month }).IsUnique();
        modelBuilder.Entity<TenantUsageMonthly>().Property(x => x.AiEstimatedCostUsd).HasPrecision(18, 6);

        modelBuilder.Entity<TenantSubscription>().HasOne(x => x.Tenant).WithMany(x => x.Subscriptions).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<TenantSubscription>().HasOne(x => x.PlanVersion).WithMany().OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<TenantAddon>().HasOne(x => x.Tenant).WithMany(x => x.Addons).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<TenantAddon>().HasOne(x => x.Addon).WithMany().OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<TenantInvitation>().HasOne(x => x.Tenant).WithMany().OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<TenantInvitation>().HasOne(x => x.Branch).WithMany().OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<TenantInvitation>().HasOne(x => x.User).WithMany().OnDelete(DeleteBehavior.Restrict);

        ApplySnakeCaseNames(modelBuilder);
    }

    private static void ApplySnakeCaseNames(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(x => ControlPlaneEntityTypes.Contains(x.ClrType)))
        {
            entityType.SetTableName(ToSnakeCase(entityType.ClrType.Name));
            foreach (var property in entityType.GetProperties())
                property.SetColumnName(ToSnakeCase(property.Name));
            foreach (var key in entityType.GetKeys())
                key.SetName($"pk_{ToSnakeCase(entityType.ClrType.Name)}");
        }
    }

    private static string ToSnakeCase(string value)
    {
        var result = new StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (index > 0 && char.IsUpper(character)) result.Append('_');
            result.Append(char.ToLowerInvariant(character));
        }
        return result.ToString();
    }
}
