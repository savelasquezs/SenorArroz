using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class ReservationDepositConfiguration : IEntityTypeConfiguration<ReservationDeposit>
{
    public void Configure(EntityTypeBuilder<ReservationDeposit> builder)
    {
        builder.ToTable("reservation_deposit");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id");

        builder.Property(d => d.OrderId).HasColumnName("order_id").IsRequired();
        builder.Property(d => d.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(d => d.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(d => d.IsEffective).HasColumnName("is_effective").IsRequired();
        builder.Property(d => d.BankId).HasColumnName("bank_id");
        builder.Property(d => d.AppId).HasColumnName("app_id");
        builder.Property(d => d.ReceivedAt).HasColumnName("received_at").IsRequired();
        builder.Property(d => d.ReceivedById).HasColumnName("received_by_id").IsRequired();
        builder.Property(d => d.Notes).HasColumnName("notes");

        builder.Property(d => d.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(d => d.Order)
            .WithMany(o => o.Deposits)
            .HasForeignKey(d => d.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.Branch)
            .WithMany()
            .HasForeignKey(d => d.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.Bank)
            .WithMany()
            .HasForeignKey(d => d.BankId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.App)
            .WithMany()
            .HasForeignKey(d => d.AppId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.ReceivedBy)
            .WithMany()
            .HasForeignKey(d => d.ReceivedById)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.OrderId);
        builder.HasIndex(d => d.BranchId);
        builder.HasIndex(d => d.ReceivedAt);
    }
}
