using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public sealed class EmployeeLeaveRequestConfiguration : IEntityTypeConfiguration<EmployeeLeaveRequest>
{
    public void Configure(EntityTypeBuilder<EmployeeLeaveRequest> b)
    {
        b.ToTable("employee_leave_request");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id");
        b.Property(x => x.PublicId).HasColumnName("public_id");
        b.Property(x => x.RequestKey).HasColumnName("request_key");
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.BranchId).HasColumnName("branch_id");
        b.Property(x => x.ApplicantName).HasColumnName("applicant_name").HasMaxLength(150).IsRequired();
        b.Property(x => x.ApplicantRole).HasColumnName("applicant_role").HasMaxLength(32).IsRequired();
        b.Property(x => x.DocumentNumber).HasColumnName("document_number").HasMaxLength(32);
        b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(40).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        b.Property(x => x.StartsAt).HasColumnName("starts_at");
        b.Property(x => x.EndsAt).HasColumnName("ends_at");
        b.Property(x => x.IsAllDay).HasColumnName("is_all_day");
        b.Property(x => x.RequestedIsPaid).HasColumnName("requested_is_paid");
        b.Property(x => x.PolicyVersion).HasColumnName("policy_version");
        b.Property(x => x.SubmissionDigest).HasColumnName("submission_digest").HasMaxLength(64);
        b.Property(x => x.Justification).HasColumnName("justification").HasMaxLength(2000).IsRequired();
        b.Property(x => x.IsPaid).HasColumnName("is_paid");
        b.Property(x => x.DecidedByUserId).HasColumnName("decided_by_user_id");
        b.Property(x => x.DecidedAt).HasColumnName("decided_at");
        b.Property(x => x.DecisionReason).HasColumnName("decision_reason").HasMaxLength(1000);
        b.Property(x => x.SupportRequired).HasColumnName("support_required");
        b.Property(x => x.SupportDueAt).HasColumnName("support_due_at");
        b.Property(x => x.SupportStatus).HasColumnName("support_status").HasMaxLength(20).IsRequired();
        b.Property(x => x.ReturnedAt).HasColumnName("returned_at");
        b.Property(x => x.ReturnVerifiedAt).HasColumnName("return_verified_at");
        b.Property(x => x.ReturnVerifiedByUserId).HasColumnName("return_verified_by_user_id");
        b.Property(x => x.VerificationReason).HasColumnName("verification_reason").HasMaxLength(1000);
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.Property(x => x.LastReadVersion).HasColumnName("last_read_version");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.PublicId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.UserId, x.RequestKey }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.BranchId, x.Status, x.CreatedAt });
        b.HasIndex(x => new { x.TenantId, x.UserId, x.CreatedAt });
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => new { x.TenantId, x.UserId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Branch).WithMany().HasForeignKey(x => new { x.TenantId, x.BranchId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.DecidedByUserId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.ReturnVerifiedByUserId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class EmployeeLeaveAttachmentConfiguration : IEntityTypeConfiguration<EmployeeLeaveAttachment>
{
    public void Configure(EntityTypeBuilder<EmployeeLeaveAttachment> b)
    {
        b.ToTable("employee_leave_attachment");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id");
        b.Property(x => x.RequestId).HasColumnName("request_id");
        b.Property(x => x.UploadKey).HasColumnName("upload_key");
        b.Property(x => x.UploadedByUserId).HasColumnName("uploaded_by_user_id");
        b.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(150).IsRequired();
        b.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(64).IsRequired();
        b.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(600).IsRequired();
        b.Property(x => x.Size).HasColumnName("size");
        b.Property(x => x.EvidenceCode).HasColumnName("evidence_code").HasMaxLength(40).IsRequired();
        b.Property(x => x.ReviewStatus).HasColumnName("review_status").HasMaxLength(16).IsRequired();
        b.Property(x => x.ReviewReason).HasColumnName("review_reason").HasMaxLength(1000);
        b.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
        b.Property(x => x.ReviewedByUserId).HasColumnName("reviewed_by_user_id");
        b.Property(x => x.ReplacesAttachmentId).HasColumnName("replaces_attachment_id");
        b.Property(x => x.SupersededAt).HasColumnName("superseded_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.HasIndex(x => new { x.TenantId, x.RequestId, x.UploadKey }).IsUnique();
        b.HasOne(x => x.Request).WithMany(x => x.Attachments)
            .HasForeignKey(x => new { x.TenantId, x.RequestId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.UploadedByUserId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.ReviewedByUserId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
