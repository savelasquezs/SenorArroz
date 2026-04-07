using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class BranchInformalLoanExemptOrderConfiguration : IEntityTypeConfiguration<BranchInformalLoanExemptOrder>
{
    public void Configure(EntityTypeBuilder<BranchInformalLoanExemptOrder> builder)
    {
        builder.ToTable("branch_informal_loan_exempt_order");

        builder.HasKey(e => new { e.LoanId, e.OrderId });

        builder.Property(e => e.LoanId).HasColumnName("loan_id");
        builder.Property(e => e.OrderId).HasColumnName("order_id");

        builder.HasOne(e => e.Loan)
            .WithMany(l => l.ExemptOrders)
            .HasForeignKey(e => e.LoanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Order)
            .WithMany()
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.OrderId).HasDatabaseName("IX_branch_informal_loan_exempt_order_order_id");
        builder.HasIndex(e => e.LoanId).HasDatabaseName("IX_branch_informal_loan_exempt_order_loan_id");
    }
}
