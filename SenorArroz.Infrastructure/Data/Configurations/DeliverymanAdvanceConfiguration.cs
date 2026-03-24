using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class DeliverymanAdvanceConfiguration : IEntityTypeConfiguration<DeliverymanAdvance>
{
    public void Configure(EntityTypeBuilder<DeliverymanAdvance> builder)
    {
        builder.ToTable("deliveryman_advance");

        builder.HasKey(da => da.Id);
        builder.Property(da => da.Id).HasColumnName("id");

        builder.Property(da => da.DeliverymanId).HasColumnName("deliveryman_id").IsRequired();
        builder.Property(da => da.Amount).HasColumnName("amount").HasColumnType("decimal(10,2)").IsRequired();
        builder.Property(da => da.PaymentMethod).HasColumnName("payment_method")
            .HasConversion<int>()
            .HasDefaultValue(DeliverymanAdvancePaymentMethod.Cash)
            .IsRequired();
        builder.Property(da => da.BankId).HasColumnName("bank_id");
        builder.Property(da => da.ExpenseHeaderId).HasColumnName("expense_header_id");
        builder.Property(da => da.Notes).HasColumnName("notes");
        builder.Property(da => da.CreatedBy).HasColumnName("created_by").IsRequired();
        builder.Property(da => da.BranchId).HasColumnName("branch_id").IsRequired();

        builder.Property(da => da.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(da => da.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        // Relaciones
        builder.HasOne(da => da.Deliveryman)
            .WithMany()
            .HasForeignKey(da => da.DeliverymanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(da => da.Creator)
            .WithMany()
            .HasForeignKey(da => da.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(da => da.Branch)
            .WithMany()
            .HasForeignKey(da => da.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(da => da.Bank)
            .WithMany()
            .HasForeignKey(da => da.BankId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(da => da.ExpenseHeader)
            .WithMany()
            .HasForeignKey(da => da.ExpenseHeaderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Índices
        builder.HasIndex(da => da.DeliverymanId).HasDatabaseName("idx_deliveryman_advance_deliveryman");
        builder.HasIndex(da => da.CreatedAt).HasDatabaseName("idx_deliveryman_advance_created_at");
        builder.HasIndex(da => da.BranchId).HasDatabaseName("idx_deliveryman_advance_branch");
        builder.HasIndex(da => da.CreatedBy).HasDatabaseName("idx_deliveryman_advance_created_by");
        builder.HasIndex(da => da.BankId).HasDatabaseName("idx_deliveryman_advance_bank");
        builder.HasIndex(da => da.ExpenseHeaderId).HasDatabaseName("idx_deliveryman_advance_expense");
    }
}

