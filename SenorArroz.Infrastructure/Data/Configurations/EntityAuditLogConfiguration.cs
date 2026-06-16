using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class EntityAuditLogConfiguration : IEntityTypeConfiguration<EntityAuditLog>
{
    public void Configure(EntityTypeBuilder<EntityAuditLog> builder)
    {
        builder.ToTable("entity_audit_log");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(50).IsRequired();
        builder.Property(x => x.EntityId).HasColumnName("entity_id").IsRequired();
        builder.Property(x => x.OperationType).HasColumnName("operation_type").HasMaxLength(50).IsRequired();
        builder.Property(x => x.BusinessDate).HasColumnName("business_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.ChangedAt).HasColumnName("changed_at").IsRequired();
        builder.Property(x => x.ChangedByUserId).HasColumnName("changed_by_user_id");
        builder.Property(x => x.ChangedByNameSnapshot).HasColumnName("changed_by_name_snapshot").HasMaxLength(200);
        builder.Property(x => x.SummaryText).HasColumnName("summary_text").HasMaxLength(500).IsRequired();
        builder.Property(x => x.MoneyDeltaJson).HasColumnName("money_delta_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.BeforeJson).HasColumnName("before_json").HasColumnType("jsonb");
        builder.Property(x => x.AfterJson).HasColumnName("after_json").HasColumnType("jsonb");
        builder.Property(x => x.MetadataJson).HasColumnName("metadata_json").HasColumnType("jsonb");

        builder.HasIndex(x => x.BranchId).HasDatabaseName("ix_entity_audit_log_branch_id");
        builder.HasIndex(x => x.BusinessDate).HasDatabaseName("ix_entity_audit_log_business_date");
        builder.HasIndex(x => x.ChangedAt).HasDatabaseName("ix_entity_audit_log_changed_at");
        builder.HasIndex(x => new { x.EntityType, x.EntityId }).HasDatabaseName("ix_entity_audit_log_entity");
    }
}
