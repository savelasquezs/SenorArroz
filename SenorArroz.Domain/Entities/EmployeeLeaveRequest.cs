using SenorArroz.Domain.Entities.Common;

namespace SenorArroz.Domain.Entities;

public sealed class EmployeeLeaveRequest : TenantOwnedEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public Guid RequestKey { get; set; }
    public int UserId { get; set; }
    public int BranchId { get; set; }
    public string ApplicantName { get; set; } = "";
    public string ApplicantRole { get; set; } = "";
    public string? DocumentNumber { get; set; }
    public string Kind { get; set; } = "other";
    public string Status { get; set; } = "pending";
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public bool IsAllDay { get; set; }
    public bool? RequestedIsPaid { get; set; }
    // Existing requests keep policy 1. Only a new submission adopts the current requirements.
    public int PolicyVersion { get; set; } = 1;
    public string? SubmissionDigest { get; set; }
    public string Justification { get; set; } = "";
    public bool? IsPaid { get; set; }
    public int? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionReason { get; set; }
    public bool SupportRequired { get; set; }
    public DateTime? SupportDueAt { get; set; }
    public string SupportStatus { get; set; } = "not_required";
    public DateTime? ReturnedAt { get; set; }
    public DateTime? ReturnVerifiedAt { get; set; }
    public int? ReturnVerifiedByUserId { get; set; }
    public string? VerificationReason { get; set; }
    public long Version { get; set; } = 1;
    public long LastReadVersion { get; set; } = 1;
    public User User { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
    public ICollection<EmployeeLeaveAttachment> Attachments { get; set; } = new List<EmployeeLeaveAttachment>();
}

public sealed class EmployeeLeaveAttachment : TenantOwnedEntity
{
    public int RequestId { get; set; }
    public Guid UploadKey { get; set; }
    public int UploadedByUserId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public int Size { get; set; }
    public string EvidenceCode { get; set; } = "additional";
    public string ReviewStatus { get; set; } = "submitted";
    public string? ReviewReason { get; set; }
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public int? ReplacesAttachmentId { get; set; }
    public DateTime? SupersededAt { get; set; }
    public EmployeeLeaveRequest Request { get; set; } = null!;
}
