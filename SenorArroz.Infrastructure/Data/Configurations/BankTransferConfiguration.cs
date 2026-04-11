using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class BankTransferConfiguration : IEntityTypeConfiguration<BankTransfer>
{
    public void Configure(EntityTypeBuilder<BankTransfer> builder)
    {
        builder.ToTable("bank_transfer");

        builder.HasKey(bt => bt.Id);
        builder.Property(bt => bt.Id).HasColumnName("id");

        builder.Property(bt => bt.FromBankId).HasColumnName("from_bank_id");
        builder.Property(bt => bt.ToBankId).HasColumnName("to_bank_id");
        builder.Property(bt => bt.Amount).HasColumnName("amount").HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(bt => bt.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(bt => bt.CreatedById).HasColumnName("created_by_id").IsRequired();

        builder.Property(bt => bt.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(bt => bt.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(bt => bt.FromBank)
            .WithMany()
            .HasForeignKey(bt => bt.FromBankId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(bt => bt.ToBank)
            .WithMany()
            .HasForeignKey(bt => bt.ToBankId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(bt => bt.CreatedBy)
            .WithMany()
            .HasForeignKey(bt => bt.CreatedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(bt => bt.FromBankId);
        builder.HasIndex(bt => bt.ToBankId);
        builder.HasIndex(bt => bt.CreatedAt);
    }
}
