using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenant");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(100).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        Timestamps(builder);
        builder.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("ux_tenant_slug");
    }

    internal static void Timestamps<T>(EntityTypeBuilder<T> builder) where T : Domain.Entities.Common.BaseEntity
    {
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()")
            .ValueGeneratedOnAddOrUpdate().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
    }
}

public sealed class CustomerPhoneConfiguration : IEntityTypeConfiguration<CustomerPhone>
{
    public void Configure(EntityTypeBuilder<CustomerPhone> builder)
    {
        builder.ToTable("customer_phone");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(x => x.PhoneNormalized).HasColumnName("phone_normalized").HasMaxLength(10).IsRequired();
        builder.Property(x => x.IsPrimary).HasColumnName("is_primary").HasDefaultValue(false);
        builder.Property(x => x.Active).HasColumnName("active").HasDefaultValue(true);
        TenantConfiguration.Timestamps(builder);
        builder.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Customer).WithMany(x => x.Phones).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.CustomerId).HasDatabaseName("ix_customer_phone_customer");
        builder.HasIndex(x => new { x.TenantId, x.PhoneNormalized }).IsUnique().HasFilter("active")
            .HasDatabaseName("ux_customer_phone_tenant_phone");
        builder.HasIndex(x => x.CustomerId).IsUnique().HasFilter("active AND is_primary")
            .HasDatabaseName("ux_customer_phone_primary");
    }
}

public sealed class AddressBranchConfiguration : IEntityTypeConfiguration<AddressBranch>
{
    public void Configure(EntityTypeBuilder<AddressBranch> builder)
    {
        builder.ToTable("address_branch");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.AddressId).HasColumnName("address_id").IsRequired();
        builder.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(x => x.NeighborhoodId).HasColumnName("neighborhood_id");
        builder.Property(x => x.DeliveryFee).HasColumnName("delivery_fee").IsRequired();
        builder.Property(x => x.IsCovered).HasColumnName("is_covered").HasDefaultValue(true);
        builder.Property(x => x.ValidatedAt).HasColumnName("validated_at");
        builder.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
        TenantConfiguration.Timestamps(builder);
        builder.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Address).WithMany(x => x.BranchServices).HasForeignKey(x => x.AddressId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Branch).WithMany(x => x.AddressServices).HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Neighborhood).WithMany(x => x.AddressServices).HasForeignKey(x => x.NeighborhoodId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.AddressId, x.BranchId }).IsUnique().HasDatabaseName("ux_address_branch_tenant_address_branch");
        builder.HasIndex(x => x.AddressId).HasDatabaseName("ix_address_branch_address");
        builder.HasIndex(x => x.BranchId).HasDatabaseName("ix_address_branch_branch");
        builder.HasIndex(x => new { x.TenantId, x.BranchId }).HasDatabaseName("ix_address_branch_tenant_branch");
    }
}

public sealed class CustomerMergeHistoryConfiguration : IEntityTypeConfiguration<CustomerMergeHistory>
{
    public void Configure(EntityTypeBuilder<CustomerMergeHistory> builder)
    {
        builder.ToTable("customer_merge_history");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.OldCustomerId).HasColumnName("old_customer_id").IsRequired();
        builder.Property(x => x.NewCustomerId).HasColumnName("new_customer_id").IsRequired();
        builder.Property(x => x.MergedAt).HasColumnName("merged_at").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(200).IsRequired();
        builder.Property(x => x.MergeRunId).HasColumnName("merge_run_id").IsRequired();
        builder.Property(x => x.DetailsJson).HasColumnName("details").HasColumnType("jsonb");
        TenantConfiguration.Timestamps(builder);
        builder.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.OldCustomer).WithMany().HasForeignKey(x => x.OldCustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.NewCustomer).WithMany().HasForeignKey(x => x.NewCustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.OldCustomerId }).IsUnique().HasDatabaseName("ux_customer_merge_history_old");
        builder.HasIndex(x => x.NewCustomerId).HasDatabaseName("ix_customer_merge_history_new");
        builder.HasIndex(x => x.MergeRunId).HasDatabaseName("ix_customer_merge_history_run");
    }
}
