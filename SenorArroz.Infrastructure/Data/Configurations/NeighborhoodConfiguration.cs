using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class NeighborhoodConfiguration : IEntityTypeConfiguration<Neighborhood>
{
    public void Configure(EntityTypeBuilder<Neighborhood> builder)
    {
        builder.ToTable("neighborhood");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).HasColumnName("id");

        builder.Property(n => n.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(n => n.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
        builder.Property(n => n.DeliveryFee).HasColumnName("delivery_fee").IsRequired();

        builder.Property(n => n.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(n => n.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasOne(n => n.Branch)
            .WithMany(b => b.Neighborhoods)
            .HasForeignKey(n => n.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
