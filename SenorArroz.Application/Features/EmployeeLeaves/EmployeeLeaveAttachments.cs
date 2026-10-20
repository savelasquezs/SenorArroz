using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Services;
using Microsoft.Extensions.Logging;

namespace SenorArroz.Application.Features.EmployeeLeaves;

public sealed partial class EmployeeLeaveCommands
{
    public async Task<Guid> Handle(AddEmployeeLeaveAttachmentCommand command, CancellationToken ct)
    {
        var input = command.Input;
        User actor;
        try { actor = await ConfirmAsync(input.Password, ct); }
        finally { input.Password = ""; }
        await using var transaction = await BeginAsync(actor.TenantId, actor.Id, ct);
        var leave = await LoadAsync(command.PublicId, ct);
        if (leave.UserId != actor.Id || leave.Status is not ("pending" or "postponed" or "approved"))
            throw new UnauthorizedAccessException();
        var file = NewAttachment(actor, leave.Kind, input);
        var previous = leave.Attachments.SingleOrDefault(x => x.UploadKey == input.UploadKey);
        if (previous is not null)
        {
            if (previous.FileName != file.FileName || previous.ContentType != file.ContentType || previous.Size != file.Size
                || previous.EvidenceCode != file.EvidenceCode || previous.ReplacesAttachmentId != input.ReplacesAttachmentId)
                throw new EmployeeLeaveConflictException();
            var saved = await storage.DownloadAsync(previous.StorageKey, previous.Size, ct);
            if (!saved.AsSpan().SequenceEqual(input.Content)) throw new EmployeeLeaveConflictException();
            return leave.PublicId;
        }
        CheckVersion(leave, input.Version);
        var replaced = input.ReplacesAttachmentId.HasValue
            ? leave.Attachments.SingleOrDefault(x => x.Id == input.ReplacesAttachmentId && x.SupersededAt == null && x.ReviewStatus == "rejected") : null;
        if (input.ReplacesAttachmentId.HasValue && (replaced is null || replaced.EvidenceCode != file.EvidenceCode))
            throw new BusinessException("Solo puedes reemplazar un soporte rechazado por otro del mismo requisito.");
        if (leave.Attachments.Count >= 50 || leave.Attachments.Count(x => x.SupersededAt == null) - (replaced is null ? 0 : 1) >= EmployeeLeaveFiles.MaxFiles)
            throw new BusinessException("Se permiten hasta cinco soportes vigentes. Usa Reemplazar para corregir un documento rechazado.");
        var before = Snapshot(leave);
        var key = await storage.UploadAsync(leave.PublicId, input.UploadKey, input.Content, file.ContentType, ct);
        try
        {
            file.StorageKey = key;
            leave.Attachments.Add(file);
            if (replaced is not null) { replaced.SupersededAt = clock.UtcNow; replaced.UpdatedAt = clock.UtcNow; }
            leave.SupportRequired = true;
            EmployeeLeaveRules.RefreshSupport(leave);
            leave.Version = checked(leave.Version + 1);
            await PersistAsync(actor, leave, replaced is null ? "attachment_added" : "attachment_replaced",
                $"Soporte {(replaced is null ? "adjunto" : "reemplazado")}: {file.FileName}", before, ct);
        }
        catch { await DeleteStorageBestEffortAsync(key); throw; }
        // Keep the object if the COMMIT response is lost; an idempotent retry checks the persisted bytes.
        if (transaction is not null) await transaction.CommitAsync(ct);
        signal.Pulse();
        return leave.PublicId;
    }

    public async Task<Guid> Handle(DeleteEmployeeLeaveAttachmentCommand command, CancellationToken ct)
    {
        var input = command.Input;
        User actor;
        try { actor = await ConfirmAsync(input.Password, ct); }
        finally { input.Password = ""; }
        await using var transaction = await BeginAsync(actor.TenantId, actor.Id, ct);
        var leave = await LoadAsync(command.PublicId, ct);
        if (!EmployeeLeaveRules.CanEdit(actor, leave)) throw new UnauthorizedAccessException();
        CheckVersion(leave, input.Version);
        var attachment = leave.Attachments.SingleOrDefault(x => x.Id == command.AttachmentId && x.SupersededAt == null)
            ?? throw new NotFoundException("Soporte no encontrado.");
        if (leave.Attachments.Any(x => x.ReplacesAttachmentId == attachment.Id))
            throw new BusinessException("El documento forma parte del historial de reemplazos.");
        var reason = EmployeeLeaveRules.RequiredText(input.Reason, 1000, "Motivo");
        var before = Snapshot(leave);
        leave.Attachments.Remove(attachment);
        EmployeeLeaveRules.RequirePriorEvidence(leave);
        db.EmployeeLeaveAttachments.Remove(attachment);
        EmployeeLeaveRules.RefreshSupport(leave);
        leave.Version = checked(leave.Version + 1);
        await PersistAsync(actor, leave, "attachment_removed", reason, before, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        signal.Pulse();
        await DeleteStorageBestEffortAsync(attachment.StorageKey);
        return leave.PublicId;
    }

    public async Task<EmployeeLeaveFile> Handle(DownloadEmployeeLeaveAttachmentQuery command, CancellationToken ct)
    {
        var actor = await EmployeeLeaveQueries.ActorAsync(db, current, ct);
        var leave = await LoadAsync(command.PublicId, ct);
        EmployeeLeaveQueries.EnsureRead(current, actor, leave);
        var file = leave.Attachments.SingleOrDefault(x => x.Id == command.AttachmentId) ?? throw new NotFoundException("Soporte no encontrado.");
        var content = await storage.DownloadAsync(file.StorageKey, file.Size, ct);
        logger.LogInformation("Employee leave attachment read. Tenant {TenantId}, actor {ActorId}, request {RequestId}, attachment {AttachmentId}.",
            actor.TenantId, actor.Id, leave.Id, file.Id);
        return new(content, file.ContentType, file.FileName);
    }

    private EmployeeLeaveAttachment NewAttachment(User actor, string kind, EmployeeLeaveNewFile input)
    {
        var file = EmployeeLeaveFiles.Validate(input.FileName ?? "", input.ContentType ?? "", input.Content);
        if (input.UploadKey == Guid.Empty) throw new BusinessException("Falta el identificador del archivo.");
        return new() { TenantId = actor.TenantId, UploadKey = input.UploadKey, UploadedByUserId = actor.Id,
            FileName = file.FileName, ContentType = file.ContentType, Size = input.Content.Length,
            EvidenceCode = EmployeeLeaveRules.EvidenceCode(kind, input.EvidenceCode), ReplacesAttachmentId = input.ReplacesAttachmentId,
            CreatedAt = clock.UtcNow, UpdatedAt = clock.UtcNow };
    }
}
