using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Services;

namespace SenorArroz.Application.Features.EmployeeLeaves;

public sealed partial class EmployeeLeaveCommands
{
    public async Task<Guid> Handle(SaveEmployeeLeaveCommand command, CancellationToken ct)
    {
        var input = command.Input;
        User actor;
        try { actor = await ConfirmAsync(input.Password, ct); }
        finally { input.Password = ""; }
        var kind = EmployeeLeaveRules.Kind(input.Kind);
        EmployeeLeaveRules.ValidateDates(input.StartsAt, input.EndsAt, input.IsAllDay);
        if (kind.IsPeriodRecord && !input.IsAllDay)
            throw new BusinessException("Registra el período de lactancia por fechas, no cada pausa.");
        if (input.RequestedIsPaid.HasValue && input.RequestedIsPaid != kind.PaidByCompanyPolicy)
            throw new BusinessException("La remuneración seleccionada no corresponde al tipo de permiso.");
        var justification = EmployeeLeaveRules.RequiredText(input.Justification, 2000, "Justificación");
        var document = string.IsNullOrWhiteSpace(input.DocumentNumber) ? null : input.DocumentNumber.Trim();
        if (document?.Length > 32) throw new BusinessException("El documento no puede superar 32 caracteres.");
        var files = command.Files ?? [];
        if (files.Count > EmployeeLeaveFiles.MaxFiles || files.Select(x => x.UploadKey).Distinct().Count() != files.Count
            || files.Any(x => x.ReplacesAttachmentId.HasValue))
            throw new BusinessException("Adjunta hasta cinco archivos diferentes. Los reemplazos se realizan desde el detalle.");
        var prepared = files.Select(x => NewAttachment(actor, input.Kind, x)).ToArray();
        var digest = SubmissionDigest(command, kind.PaidByCompanyPolicy, justification, document);
        await using var transaction = await BeginAsync(actor.TenantId, actor.Id, ct);
        EmployeeLeaveRequest leave;
        string? before = null;
        if (command.PublicId.HasValue)
        {
            leave = await LoadAsync(command.PublicId.Value, ct);
            if (leave.UserId != actor.Id) throw new UnauthorizedAccessException();
            if (leave.SubmissionDigest == digest) return leave.PublicId;
            if (!EmployeeLeaveRules.CanEdit(actor, leave)) throw new UnauthorizedAccessException();
            CheckVersion(leave, input.Version);
            before = Snapshot(leave);
            var differentKind = leave.Kind != input.Kind;
            var differentInterval = leave.StartsAt != input.StartsAt || leave.EndsAt != input.EndsAt || leave.IsAllDay != input.IsAllDay;
            if (differentKind || differentInterval)
            {
                foreach (var file in leave.Attachments.Where(x => x.SupersededAt == null))
                {
                    if (differentKind) file.EvidenceCode = "additional";
                    // A verified document for the old appointment cannot approve new dates automatically.
                    // A previously rejected file remains rejected until the employee replaces it.
                    if (file.ReviewStatus == "verified")
                    {
                        file.ReviewStatus = "submitted"; file.ReviewReason = null;
                        file.ReviewedByUserId = null; file.ReviewedAt = null; file.UpdatedAt = clock.UtcNow;
                    }
                }
                leave.SupportDueAt = null;
                if (differentKind) leave.SupportRequired = false;
            }
            leave.Version = checked(leave.Version + 1);
            leave.Status = "pending";
            leave.DecisionReason = null;
            leave.DecidedAt = null;
            leave.DecidedByUserId = null;
            leave.IsPaid = null;
        }
        else
        {
            if (input.RequestKey == Guid.Empty) throw new BusinessException("Falta el identificador de la solicitud.");
            var previous = await db.EmployeeLeaveRequests.SingleOrDefaultAsync(x => x.UserId == actor.Id && x.RequestKey == input.RequestKey, ct);
            if (previous is not null)
            {
                if (previous.SubmissionDigest == digest) return previous.PublicId;
                if (previous.SubmissionDigest == null && files.Count == 0 && previous.Kind == input.Kind
                    && previous.StartsAt == input.StartsAt && previous.EndsAt == input.EndsAt
                    && previous.IsAllDay == input.IsAllDay && previous.Justification == justification && previous.DocumentNumber == document)
                    return previous.PublicId;
                throw new EmployeeLeaveConflictException();
            }
            leave = new EmployeeLeaveRequest { TenantId = actor.TenantId, UserId = actor.Id, BranchId = actor.BranchId,
                RequestKey = input.RequestKey, ApplicantName = actor.Name, ApplicantRole = actor.Role?.ToString() ?? "", CreatedAt = clock.UtcNow };
            db.EmployeeLeaveRequests.Add(leave);
        }
        if (leave.Attachments.Count(x => x.SupersededAt == null) + files.Count > EmployeeLeaveFiles.MaxFiles)
            throw new BusinessException("Se permiten hasta cinco soportes vigentes por solicitud.");
        if (prepared.Any(x => leave.Attachments.Any(a => a.UploadKey == x.UploadKey))) throw new EmployeeLeaveConflictException();
        leave.Kind = input.Kind;
        leave.DocumentNumber = document;
        leave.StartsAt = input.StartsAt;
        leave.EndsAt = input.EndsAt;
        leave.IsAllDay = input.IsAllDay;
        leave.RequestedIsPaid = kind.PaidByCompanyPolicy;
        leave.PolicyVersion = EmployeeLeaveRules.CurrentPolicyVersion;
        leave.SubmissionDigest = digest;
        leave.Justification = justification;
        foreach (var file in prepared) leave.Attachments.Add(file);
        EmployeeLeaveRules.RequirePriorEvidence(leave);
        EmployeeLeaveRules.RefreshSupport(leave);
        var uploaded = new List<string>();
        try
        {
            for (var i = 0; i < prepared.Length; i++)
            {
                prepared[i].StorageKey = await storage.UploadAsync(leave.PublicId, prepared[i].UploadKey, files[i].Content, prepared[i].ContentType, ct);
                uploaded.Add(prepared[i].StorageKey);
            }
            // No reviewable request or outbox message is persisted for a partial upload.
            await PersistAsync(actor, leave, before is null ? "created" : "resubmitted", "Solicitud y soportes enviados por el trabajador.", before, ct);
        }
        catch
        {
            foreach (var key in uploaded) await DeleteStorageBestEffortAsync(key);
            throw;
        }
        // An ambiguous COMMIT must NOT delete files that may already be referenced. RequestKey/digest reconciles replay.
        if (transaction is not null) await transaction.CommitAsync(ct);
        signal.Pulse();
        return leave.PublicId;
    }

    private static string SubmissionDigest(SaveEmployeeLeaveCommand command, bool paid, string justification, string? document) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            command.PublicId, command.Input.RequestKey, command.Input.Version, command.Input.Kind,
            command.Input.StartsAt, command.Input.EndsAt, command.Input.IsAllDay, paid, justification, document,
            Files = (command.Files ?? []).OrderBy(x => x.UploadKey).Select(x => new { x.UploadKey, x.FileName, x.ContentType,
                EvidenceCode = string.IsNullOrWhiteSpace(x.EvidenceCode) ? "additional" : x.EvidenceCode.Trim(),
                Digest = Convert.ToHexString(SHA256.HashData(x.Content)) }).ToArray()
        })));
}
