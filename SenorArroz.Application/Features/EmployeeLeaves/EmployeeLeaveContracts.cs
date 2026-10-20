using MediatR;
using SenorArroz.Domain.Services;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.EmployeeLeaves;

public sealed record EmployeeLeaveListDto(Guid PublicId, int UserId, int BranchId, string BranchName,
    string ApplicantName, string ApplicantRole, string Kind, string Status, DateTime StartsAt,
    DateTime EndsAt, DateTime CreatedAt, DateTime UpdatedAt, long Version, bool IsUnread,
    bool IsAllDay = false, bool RequestedIsPaid = false, bool? IsPaid = null, string SupportStatus = "not_required", bool IsPeriodRecord = false);
public sealed record EmployeeLeaveAttachmentDto(int Id, string FileName, string ContentType, int Size, DateTime CreatedAt,
    string EvidenceCode = "additional", string ReviewStatus = "submitted", string? ReviewReason = null,
    DateTime? ReviewedAt = null, DateTime? SupersededAt = null, int? ReplacesAttachmentId = null);
public sealed record EmployeeLeaveHistoryDto(int Id, string Operation, DateTime ChangedAt, string ActorName, string Reason);
public sealed record EmployeeLeaveSummaryDto(int Unread, int PendingReview);
public sealed record EmployeeLeaveDetailDto(EmployeeLeaveListDto Request, string? DocumentNumber,
    string Justification, bool? IsPaid, string? DecisionReason, DateTime? DecidedAt, string? DecidedByName,
    bool SupportRequired, DateTime? SupportDueAt, string SupportStatus, DateTime? ReturnedAt,
    DateTime? ReturnVerifiedAt, string? VerificationReason, bool CanEdit, bool CanReview,
    bool CanCancel, bool CanReturn, bool HasReviewer, List<EmployeeLeaveAttachmentDto> Attachments,
    EmployeeLeaveEvidenceState[]? EvidenceRequirements = null, int PolicyVersion = 1);
public sealed record GetEmployeeLeavesQuery(bool Review = false, string? Status = null, string? Search = null,
    int? BranchId = null, DateTime? From = null, DateTime? To = null, int Page = 1, int PageSize = 20,
    bool? Remunerated = null, string? Support = null) : IRequest<PagedResult<EmployeeLeaveListDto>>;
public sealed record GetEmployeeLeaveQuery(Guid PublicId) : IRequest<EmployeeLeaveDetailDto>;
public sealed record GetEmployeeLeaveSummaryQuery : IRequest<EmployeeLeaveSummaryDto>;
public sealed record GetEmployeeLeaveHistoryQuery(Guid PublicId, int Page = 1, int PageSize = 25) : IRequest<PagedResult<EmployeeLeaveHistoryDto>>;
public sealed record MarkEmployeeLeaveReadCommand(Guid PublicId, long Version) : IRequest<Unit>;

// Password-bearing DTOs must never be logged, persisted, or emitted by record.ToString().
public sealed class EmployeeLeaveInput
{
    public Guid RequestKey { get; set; }
    public long Version { get; set; }
    public string Password { get; set; } = "";
    public string Kind { get; set; } = "other";
    public string? DocumentNumber { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public bool IsAllDay { get; set; }
    public bool? RequestedIsPaid { get; set; }
    public string Justification { get; set; } = "";
    public bool SupportRequired { get; set; }
}
public sealed class EmployeeLeaveAction
{
    public long Version { get; set; }
    public string Password { get; set; } = "";
    public string? Status { get; set; }
    public string Reason { get; set; } = "";
    public bool? IsPaid { get; set; }
    public bool SupportRequired { get; set; }
    public DateTime? SupportDueAt { get; set; }
    public string? SupportStatus { get; set; }
    public int? AttachmentId { get; set; }
    public bool VerifyReturn { get; set; }
}
public class EmployeeLeaveNewFile
{
    public Guid UploadKey { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public string EvidenceCode { get; set; } = "additional";
    public int? ReplacesAttachmentId { get; set; }
}
public sealed class EmployeeLeaveUpload : EmployeeLeaveNewFile
{
    public long Version { get; set; }
    public string Password { get; set; } = "";
}
public sealed record SaveEmployeeLeaveCommand(Guid? PublicId, EmployeeLeaveInput Input,
    IReadOnlyList<EmployeeLeaveNewFile>? Files = null) : IRequest<Guid>;
public sealed record ChangeEmployeeLeaveCommand(Guid PublicId, string Action, EmployeeLeaveAction Input) : IRequest<Guid>;
public sealed record AddEmployeeLeaveAttachmentCommand(Guid PublicId, EmployeeLeaveUpload Input) : IRequest<Guid>;
public sealed record DeleteEmployeeLeaveAttachmentCommand(Guid PublicId, int AttachmentId, EmployeeLeaveAction Input) : IRequest<Guid>;
public sealed record DownloadEmployeeLeaveAttachmentQuery(Guid PublicId, int AttachmentId) : IRequest<EmployeeLeaveFile>;
public sealed record EmployeeLeaveFile(byte[] Content, string ContentType, string FileName);
public interface IEmployeeLeaveStorage
{
    Task<string> UploadAsync(Guid publicId, Guid uploadKey, byte[] content, string contentType, CancellationToken ct);
    Task<byte[]> DownloadAsync(string storageKey, int expectedSize, CancellationToken ct);
    Task DeleteAsync(string storageKey, CancellationToken ct);
}
public sealed class EmployeeLeaveConflictException : Exception
{
    public EmployeeLeaveConflictException() : base("La solicitud cambió. Actualiza el detalle antes de confirmar de nuevo.") { }
}
