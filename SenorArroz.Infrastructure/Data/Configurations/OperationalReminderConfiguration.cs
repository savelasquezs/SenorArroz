using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public sealed class OperationalReminderConfiguration : IEntityTypeConfiguration<OperationalReminder>
{
    public void Configure(EntityTypeBuilder<OperationalReminder> b)
    {
        b.ToTable("operational_reminder");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.BranchId).HasColumnName("branch_id");
        b.Property(x => x.Code).HasColumnName("code").HasMaxLength(64);
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(160);
        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000);
        b.Property(x => x.Condition).HasColumnName("condition_text").HasMaxLength(500);
        b.Property(x => x.Period).HasColumnName("period").HasMaxLength(10);
        b.Property(x => x.DaysOfWeek).HasColumnName("days_of_week");
        b.Property(x => x.ShiftMondayHoliday).HasColumnName("shift_monday_holiday");
        b.Property(x => x.Responsible).HasColumnName("responsible").HasMaxLength(12);
        b.Property(x => x.ManualPath).HasColumnName("manual_path").HasMaxLength(250);
        b.Property(x => x.SortOrder).HasColumnName("sort_order");
        b.Property(x => x.IsActive).HasColumnName("is_active");
        b.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken();
        b.Property(x => x.SyncedRevision).HasColumnName("synced_revision");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasIndex(x => new { x.TenantId, x.BranchId, x.Code }).IsUnique();
        b.HasAlternateKey(x => new { x.TenantId, x.BranchId, x.Id });
        b.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.State).WithOne().HasForeignKey<OperationalReminderState>(x => new { x.TenantId, x.BranchId, x.ReminderId })
            .HasPrincipalKey<OperationalReminder>(x => new { x.TenantId, x.BranchId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class OperationalReminderStateConfiguration : IEntityTypeConfiguration<OperationalReminderState>
{
    public void Configure(EntityTypeBuilder<OperationalReminderState> b)
    {
        b.ToTable("operational_reminder_state");
        b.HasKey(x => new { x.TenantId, x.ReminderId });
        b.Property(x => x.BranchId).HasColumnName("branch_id");
        b.Property(x => x.ReminderId).HasColumnName("reminder_id");
        b.Property(x => x.OperationDate).HasColumnName("operation_date");
        b.Property(x => x.IsChecked).HasColumnName("is_checked");
        b.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken();
    }
}
