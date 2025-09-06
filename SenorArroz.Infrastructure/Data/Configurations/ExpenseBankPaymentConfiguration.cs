using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class ExpenseBankPaymentConfiguration : IEntityTypeConfiguration<ExpenseBankPayment>
{
    public void Configure(EntityTypeBuilder<ExpenseBankPayment> builder)
    {
        builder.ToTable("expense_bank_payment");

        builder.HasKey(ebp => ebp.Id);
        builder.Property(ebp => ebp.Id).HasColumnName("id");

        builder.Property(ebp => ebp.BankId).HasColumnName("bank_id").IsRequired();
        builder.Property(ebp => ebp.ExpenseHeaderId).HasColumnName("expense_header_id").IsRequired();
        builder.Property(ebp => ebp.Amount).HasColumnName("amount").HasColumnType("numeric(12,2)").IsRequired();

        builder.Property(ebp => ebp.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(ebp => ebp.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        // Relaciones
        builder.HasOne(ebp => ebp.Bank)
            .WithMany(b => b.ExpenseBankPayments)
            .HasForeignKey(ebp => ebp.BankId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(ebp => ebp.ExpenseHeader)
            .WithMany(eh => eh.ExpenseBankPayments)
            .HasForeignKey(ebp => ebp.ExpenseHeaderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}