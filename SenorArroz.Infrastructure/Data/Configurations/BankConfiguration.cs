using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class BankConfiguration : IEntityTypeConfiguration<Bank>
{
    public void Configure(EntityTypeBuilder<Bank> builder)
    {
        builder.ToTable("bank");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id");

        builder.Property(b => b.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(b => b.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
        builder.Property(b => b.ImageUrl).HasColumnName("image_url").HasMaxLength(200);
        builder.Property(b => b.Active).HasColumnName("active").HasDefaultValue(true);

        builder.Property(b => b.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(b => b.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasOne(b => b.Branch)
            .WithMany(br => br.Banks)
            .HasForeignKey(b => b.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}