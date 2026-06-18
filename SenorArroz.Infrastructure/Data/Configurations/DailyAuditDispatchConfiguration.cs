using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class DailyAuditDispatchConfiguration : IEntityTypeConfiguration<DailyAuditDispatch>
{
    public void Configure(EntityTypeBuilder<DailyAuditDispatch> builder)
    {
        builder.ToTable("daily_audit_dispatch");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(x => x.BusinessDate).HasColumnName("business_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.CashRegisterClosureId).HasColumnName("cash_register_closure_id").IsRequired();
        builder.Property(x => x.DispatchedAt).HasColumnName("dispatched_at");
        builder.Property(x => x.DispatchedByUserId).HasColumnName("dispatched_by_user_id");
        builder.Property(x => x.DispatchStatus).HasColumnName("dispatch_status").HasMaxLength(50).IsRequired();
        builder.Property(x => x.DispatchError).HasColumnName("dispatch_error").HasMaxLength(2000);
        builder.Property(x => x.RecipientEmailsJson).HasColumnName("recipient_emails_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.SummaryJson).HasColumnName("summary_json").HasColumnType("jsonb").IsRequired();

        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CashRegisterClosure)
            .WithMany()
            .HasForeignKey(x => x.CashRegisterClosureId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.DispatchedByUser)
            .WithMany()
            .HasForeignKey(x => x.DispatchedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.BranchId, x.BusinessDate })
            .IsUnique()
            .HasDatabaseName("ux_daily_audit_dispatch_branch_business_date");
        builder.HasIndex(x => x.CashRegisterClosureId).HasDatabaseName("ix_daily_audit_dispatch_closure_id");
    }
}
