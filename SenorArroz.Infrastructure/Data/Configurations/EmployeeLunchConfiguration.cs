using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public sealed class EmployeeLunchConfiguration : IEntityTypeConfiguration<EmployeeLunch>
{
    public void Configure(EntityTypeBuilder<EmployeeLunch> b)
    {
        b.ToTable("employee_lunch");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.BranchId).HasColumnName("branch_id");
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.StartedAt).HasColumnName("started_at");
        b.Property(x => x.EndedAt).HasColumnName("ended_at");
        b.Property(x => x.StartedByUserId).HasColumnName("started_by_user_id");
        b.Property(x => x.EndedByUserId).HasColumnName("ended_by_user_id");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsConcurrencyToken();
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => new { x.TenantId, x.UserId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.StartedByUserId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.EndedByUserId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique().HasFilter("ended_at IS NULL");
        b.HasIndex(x => new { x.TenantId, x.BranchId, x.StartedAt });
    }
}
