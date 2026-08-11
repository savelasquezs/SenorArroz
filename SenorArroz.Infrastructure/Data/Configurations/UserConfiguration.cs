using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("user");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id");

        builder.Property(u => u.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(u => u.Role)
            .HasColumnName("role")
            .HasConversion(
                v => v.ToString().ToLower(),
                v => Enum.Parse<UserRole>(v, true))
            .HasDefaultValue(UserRole.Deliveryman);
        builder.Property(u => u.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
        builder.Property(u => u.Email).HasColumnName("email").HasMaxLength(100).IsRequired();
        builder.Property(u => u.Phone).HasColumnName("phone").HasMaxLength(10).IsRequired();
        builder.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(255).IsRequired();
        builder.Property(u => u.Active).HasColumnName("active").HasDefaultValue(true);
        builder.Property(u => u.WebAccessEnabled).HasColumnName("web_access_enabled").HasDefaultValue(true);
        builder.Property(u => u.ProfileImageUrl).HasColumnName("profile_image_url").HasMaxLength(2000);
        builder.Property(u => u.ActiveSessionId).HasColumnName("active_session_id");
        builder.Property(u => u.PayrollExpenseId).HasColumnName("payroll_expense_id");

        builder.Property(u => u.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate()
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.HasOne(u => u.Branch)
            .WithMany(b => b.Users)
            .HasForeignKey(u => u.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(u => u.PayrollExpense)
            .WithMany()
            .HasForeignKey(u => u.PayrollExpenseId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(u => u.BranchId).HasDatabaseName("idx_user_branch");
        builder.HasIndex(u => u.PayrollExpenseId)
            .IsUnique()
            .HasFilter("payroll_expense_id IS NOT NULL")
            .HasDatabaseName("IX_user_payroll_expense_id_unique");
        builder.HasIndex(u => u.Role).HasDatabaseName("idx_user_role");
        builder.HasIndex(u => u.Active).HasDatabaseName("idx_user_active").HasFilter("active = true");
    }
}
