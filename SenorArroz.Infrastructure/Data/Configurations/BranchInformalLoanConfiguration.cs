using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class BranchInformalLoanConfiguration : IEntityTypeConfiguration<BranchInformalLoan>
{
    public void Configure(EntityTypeBuilder<BranchInformalLoan> builder)
    {
        builder.ToTable("branch_informal_loan");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(e => e.Concept).HasColumnName("concept").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Amount).HasColumnName("amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(e => e.CreatedById).HasColumnName("created_by_id").IsRequired();

        builder.Property(e => e.DeactivatedAt).HasColumnName("deactivated_at");
        builder.Property(e => e.DeactivatedById).HasColumnName("deactivated_by_id");
        builder.Property(e => e.DeactivationNotes).HasColumnName("deactivation_notes").HasMaxLength(500);

        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(e => e.Branch)
            .WithMany()
            .HasForeignKey(e => e.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.CreatedBy)
            .WithMany()
            .HasForeignKey(e => e.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.DeactivatedBy)
            .WithMany()
            .HasForeignKey(e => e.DeactivatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.BranchId);
        builder.HasIndex(e => new { e.BranchId, e.DeactivatedAt });
    }
}
