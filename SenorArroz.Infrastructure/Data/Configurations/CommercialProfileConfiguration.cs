using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class CommercialProfileConfiguration : IEntityTypeConfiguration<CommercialProfile>
{
    public void Configure(EntityTypeBuilder<CommercialProfile> builder)
    {
        builder.ToTable("commercial_profile");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000);
        builder.Property(x => x.Ingredients).HasColumnName("ingredients").HasMaxLength(2000);
        builder.Property(x => x.PhotoUrl).HasColumnName("photo_url").HasMaxLength(2000);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.HasOne(x => x.Branch).WithMany(x => x.CommercialProfiles).HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.BranchId, x.Name }).IsUnique().HasDatabaseName("ux_commercial_profile_branch_name");
    }
}
