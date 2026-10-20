using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Services;
using SenorArroz.Domain.Services;

namespace SenorArroz.Application.Features.EmployeeLeaves;

public sealed class EmployeeLeaveCommands(IApplicationDbContext db, ICurrentUser current,
    IPasswordService passwords, IClock clock, IConfiguration configuration, IEmployeeLeaveStorage storage,
    IBackgroundWorkSignal<EmailOutboxWork> signal, ILogger<EmployeeLeaveCommands> logger)
    : IRequestHandler<SaveEmployeeLeaveCommand, Guid>, IRequestHandler<ChangeEmployeeLeaveCommand, Guid>,
      IRequestHandler<MarkEmployeeLeaveReadCommand, Unit>, IRequestHandler<AddEmployeeLeaveAttachmentCommand, Guid>,
      IRequestHandler<DeleteEmployeeLeaveAttachmentCommand, Guid>,
      IRequestHandler<DownloadEmployeeLeaveAttachmentQuery, EmployeeLeaveFile>
{
    public async Task<Guid> Handle(SaveEmployeeLeaveCommand command, CancellationToken ct)
    {
        var input = command.Input;
        User actor;
        try { actor = await ConfirmAsync(input.Password, ct); }
        finally { input.Password = ""; }
        EmployeeLeaveRules.ValidateDates(input.StartsAt, input.EndsAt);
        EmployeeLeaveRules.Kind(input.Kind);
        var justification = EmployeeLeaveRules.RequiredText(input.Justification, 2000, "Justificación");
        var document = string.IsNullOrWhiteSpace(input.DocumentNumber) ? null : input.DocumentNumber.Trim();
        if (document?.Length > 32) throw new BusinessException("El documento no puede superar 32 caracteres.");
        await using var transaction = await BeginAsync(actor.TenantId, actor.Id, ct);
        EmployeeLeaveRequest leave;
        string? before = null;
        if (command.PublicId.HasValue)
        {
            leave = await LoadAsync(command.PublicId.Value, ct);
            if (!EmployeeLeaveRules.CanEdit(actor, leave)) throw new UnauthorizedAccessException();
            CheckVersion(leave, input.Version);
            before = Snapshot(leave);
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
                if (previous.Kind != input.Kind || previous.StartsAt != input.StartsAt || previous.EndsAt != input.EndsAt
                    || previous.Justification != justification || previous.DocumentNumber != document)
                    throw new EmployeeLeaveConflictException();
                return previous.PublicId;
            }
            leave = new EmployeeLeaveRequest
            {
                TenantId = actor.TenantId, UserId = actor.Id, BranchId = actor.BranchId, RequestKey = input.RequestKey,
                ApplicantName = actor.Name, ApplicantRole = actor.Role?.ToString() ?? "", CreatedAt = clock.UtcNow
            };
            db.EmployeeLeaveRequests.Add(leave);
        }
        leave.Kind = input.Kind;
        leave.DocumentNumber = document;
        leave.StartsAt = input.StartsAt;
        leave.EndsAt = input.EndsAt;
        leave.Justification = justification;
        leave.SupportRequired |= input.SupportRequired;
        if (leave.SupportRequired && leave.SupportStatus == "not_required") leave.SupportStatus = "pending";
        await PersistAsync(actor, leave, before is null ? "created" : "resubmitted", "Solicitud enviada por el trabajador.", before, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        signal.Pulse();
        return leave.PublicId;
    }

    public async Task<Guid> Handle(ChangeEmployeeLeaveCommand command, CancellationToken ct)
    {
        var input = command.Input;
        User actor;
        try { actor = await ConfirmAsync(input.Password, ct); }
        finally { input.Password = ""; }
        // Autorizar sin seguimiento antes del bloqueo; cargar la versión mutable DESPUÉS de adquirirlo.
        var scope = await db.EmployeeLeaveRequests.AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == command.PublicId, ct)
            ?? throw new NotFoundException("Permiso no encontrado.");
        EmployeeLeaveQueries.EnsureRead(current, actor, scope);
        await using var transaction = await BeginAsync(actor.TenantId, scope.UserId, ct);
        var leave = await LoadAsync(command.PublicId, ct);
        EmployeeLeaveQueries.EnsureRead(current, actor, leave);
        CheckVersion(leave, input.Version);
        var before = Snapshot(leave);
        var review = EmployeeLeaveQueries.CanReview(current, actor, leave);
        var own = leave.UserId == actor.Id;
        var reason = command.Action == "return" ? "Regreso confirmado por el trabajador."
            : EmployeeLeaveRules.RequiredText(input.Reason, 1000, "Motivo");
        switch (command.Action)
        {
            case "decision":
                if (!review || leave.Status is not ("pending" or "postponed")) throw new UnauthorizedAccessException();
                EmployeeLeaveRules.ValidateDecision(input.Status ?? "", leave.Kind, input.IsPaid);
                if (input.Status == "approved")
                {
                    if (input.SupportDueAt is { Kind: not DateTimeKind.Utc }
                        || input.SupportDueAt.HasValue && input.SupportDueAt < clock.UtcNow)
                        throw new BusinessException("El plazo para presentar soporte debe ser una fecha UTC vigente.");
                    if (await db.EmployeeLeaveRequests.AnyAsync(x => x.UserId == leave.UserId
                        && x.Id != leave.Id && x.Status == "approved" && x.StartsAt < leave.EndsAt && x.EndsAt > leave.StartsAt, ct))
                        throw new BusinessException("El trabajador ya tiene un permiso aprobado que se cruza con este intervalo.");
                    leave.SupportRequired |= input.SupportRequired;
                    leave.SupportDueAt = leave.SupportRequired ? input.SupportDueAt : null;
                    if (leave.SupportRequired && leave.SupportStatus == "not_required") leave.SupportStatus = "pending";
                }
                leave.Status = input.Status!;
                leave.IsPaid = input.Status == "approved" ? input.IsPaid : null;
                leave.DecisionReason = reason;
                leave.DecidedAt = clock.UtcNow;
                leave.DecidedByUserId = actor.Id;
                break;
            case "cancel":
                if (!(own || review) || !(leave.Status is "pending" or "postponed"
                    || leave.Status == "approved" && leave.StartsAt > clock.UtcNow && leave.ReturnedAt == null))
                    throw new BusinessException("Esta solicitud ya no puede cancelarse.");
                leave.Status = "cancelled";
                leave.DecisionReason = reason;
                leave.DecidedAt = clock.UtcNow;
                leave.DecidedByUserId = actor.Id;
                break;
            case "return":
                if (!own || leave.Status != "approved" || leave.ReturnedAt.HasValue || leave.StartsAt > clock.UtcNow)
                    throw new BusinessException("No se puede registrar el regreso de este permiso.");
                leave.ReturnedAt = clock.UtcNow;
                break;
            case "verify":
                if (!review || leave.Status != "approved") throw new UnauthorizedAccessException();
                if (input.SupportStatus is not ("verified" or "rejected" or null))
                    throw new BusinessException("Verificación de soporte no válida.");
                if (input.SupportStatus == "verified" && leave.Attachments.Count == 0)
                    throw new BusinessException("No hay soportes adjuntos para verificar.");
                if (input.SupportStatus is not null) leave.SupportStatus = input.SupportStatus;
                if (input.VerifyReturn)
                {
                    if (!leave.ReturnedAt.HasValue) throw new BusinessException("El trabajador aún no ha registrado su regreso.");
                    leave.ReturnVerifiedAt = clock.UtcNow;
                    leave.ReturnVerifiedByUserId = actor.Id;
                }
                if (input.SupportStatus is null && !input.VerifyReturn) throw new BusinessException("Selecciona qué deseas verificar.");
                leave.VerificationReason = reason;
                break;
            default:
                throw new BusinessException("Acción no válida.");
        }
        leave.Version = checked(leave.Version + 1);
        await PersistAsync(actor, leave, command.Action, reason, before, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        signal.Pulse();
        return leave.PublicId;
    }

    public async Task<Unit> Handle(MarkEmployeeLeaveReadCommand command, CancellationToken ct)
    {
        var actor = await EmployeeLeaveQueries.ActorAsync(db, current, ct);
        await using var transaction = await BeginAsync(actor.TenantId, actor.Id, ct);
        var leave = await LoadAsync(command.PublicId, ct);
        if (leave.UserId != actor.Id) throw new NotFoundException("Permiso no encontrado.");
        if (command.Version < 1 || command.Version > leave.Version) throw new EmployeeLeaveConflictException();
        leave.LastReadVersion = Math.Max(leave.LastReadVersion, command.Version);
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Unit.Value;
    }

    public async Task<Guid> Handle(AddEmployeeLeaveAttachmentCommand command, CancellationToken ct)
    {
        var input = command.Input;
        User actor;
        try { actor = await ConfirmAsync(input.Password, ct); }
        finally { input.Password = ""; }
        var file = EmployeeLeaveFiles.Validate(input.FileName, input.ContentType, input.Content);
        if (input.UploadKey == Guid.Empty) throw new BusinessException("Falta el identificador del archivo.");
        await using var transaction = await BeginAsync(actor.TenantId, actor.Id, ct);
        var leave = await LoadAsync(command.PublicId, ct);
        if (leave.UserId != actor.Id || leave.Status is not ("pending" or "postponed" or "approved"))
            throw new UnauthorizedAccessException();
        var previous = leave.Attachments.SingleOrDefault(x => x.UploadKey == input.UploadKey);
        if (previous is not null)
        {
            if (previous.FileName != file.FileName || previous.ContentType != file.ContentType || previous.Size != input.Content.Length)
                throw new EmployeeLeaveConflictException();
            // El nombre y el tamaño no identifican los bytes: un reintento debe ser el mismo soporte.
            var saved = await storage.DownloadAsync(previous.StorageKey, previous.Size, ct);
            if (!saved.AsSpan().SequenceEqual(input.Content)) throw new EmployeeLeaveConflictException();
            return leave.PublicId;
        }
        CheckVersion(leave, input.Version);
        if (leave.Attachments.Count >= EmployeeLeaveFiles.MaxFiles) throw new BusinessException("Se permiten hasta cinco soportes por solicitud.");
        var before = Snapshot(leave);
        var key = await storage.UploadAsync(leave.PublicId, input.UploadKey, input.Content, file.ContentType, ct);
        try
        {
            leave.Attachments.Add(new EmployeeLeaveAttachment
            {
                TenantId = actor.TenantId, UploadKey = input.UploadKey, UploadedByUserId = actor.Id,
                FileName = file.FileName, ContentType = file.ContentType, Size = input.Content.Length,
                StorageKey = key, CreatedAt = clock.UtcNow, UpdatedAt = clock.UtcNow
            });
            leave.SupportRequired = true;
            leave.SupportStatus = "submitted";
            leave.Version = checked(leave.Version + 1);
            await PersistAsync(actor, leave, "attachment_added", $"Soporte adjunto: {file.FileName}", before, ct);
        }
        catch
        {
            await DeleteStorageBestEffortAsync(key);
            throw;
        }
        // Si se pierde la confirmación del COMMIT, conservar el objeto: puede estar ya referenciado.
        // Reintentar con la misma UploadKey consulta el registro, sin duplicar ni borrar el archivo confirmado.
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
        var attachment = leave.Attachments.SingleOrDefault(x => x.Id == command.AttachmentId)
            ?? throw new NotFoundException("Soporte no encontrado.");
        var reason = EmployeeLeaveRules.RequiredText(input.Reason, 1000, "Motivo");
        var before = Snapshot(leave);
        db.EmployeeLeaveAttachments.Remove(attachment);
        leave.Attachments.Remove(attachment);
        leave.SupportStatus = leave.Attachments.Count > 0 ? "submitted" : leave.SupportRequired ? "pending" : "not_required";
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
        var file = leave.Attachments.SingleOrDefault(x => x.Id == command.AttachmentId)
            ?? throw new NotFoundException("Soporte no encontrado.");
        var content = await storage.DownloadAsync(file.StorageKey, file.Size, ct);
        logger.LogInformation("Employee leave attachment read. Tenant {TenantId}, actor {ActorId}, request {RequestId}, attachment {AttachmentId}.",
            actor.TenantId, actor.Id, leave.Id, file.Id);
        return new(content, file.ContentType, file.FileName);
    }

    private async Task<User> ConfirmAsync(string password, CancellationToken ct)
    {
        var actor = await EmployeeLeaveQueries.ActorAsync(db, current, ct);
        EmployeePasswordConfirmation.Verify(actor, password, passwords);
        return actor;
    }

    private async Task<EmployeeLeaveRequest> LoadAsync(Guid publicId, CancellationToken ct) =>
        await db.EmployeeLeaveRequests.Include(x => x.Attachments).Include(x => x.Branch)
            .SingleOrDefaultAsync(x => x.PublicId == publicId, ct)
        ?? throw new NotFoundException("Permiso no encontrado.");

    private async Task<IDbContextTransaction?> BeginAsync(int tenantId, int userId, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return null;
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Serializa únicamente los permisos de la misma persona, no pedidos ni cocina.
            if (db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({tenantId}, {userId});", ct);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    private static void CheckVersion(EmployeeLeaveRequest leave, long expected)
    {
        if (expected < 1 || leave.Version != expected) throw new EmployeeLeaveConflictException();
    }

    private async Task PersistAsync(User actor, EmployeeLeaveRequest leave, string operation, string reason, string? before, CancellationToken ct)
    {
        leave.UpdatedAt = clock.UtcNow;
        if (actor.Id == leave.UserId) leave.LastReadVersion = leave.Version;
        await db.SaveChangesAsync(ct);
        db.EntityAuditLogs.Add(new EntityAuditLog
        {
            TenantId = leave.TenantId, BranchId = leave.BranchId, EntityType = "employee_leave_request",
            EntityId = leave.Id, OperationType = operation, ChangedAt = clock.UtcNow,
            BusinessDate = clock.UtcNow.AddHours(-5).Date, ChangedByUserId = actor.Id, ChangedByNameSnapshot = actor.Name,
            SummaryText = reason, BeforeJson = before, AfterJson = Snapshot(leave),
            MetadataJson = "{\"confirmation\":\"current_password\",\"channel\":\"application\"}"
        });
        await EmployeeLeaveEmail.EnqueueAsync(db, configuration, leave, actor.Id, operation, clock.UtcNow, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task DeleteStorageBestEffortAsync(string key)
    {
        try
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await storage.DeleteAsync(key, cleanup.Token);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Private employee attachment cleanup pending for {StorageKey}.", key); }
    }

    private static string Snapshot(EmployeeLeaveRequest x) => JsonSerializer.Serialize(new
    {
        x.PublicId, x.Version, x.Kind, x.Status, x.StartsAt, x.EndsAt, x.Justification, x.DocumentNumber,
        x.IsPaid, x.DecisionReason, x.DecidedByUserId, x.DecidedAt, x.SupportRequired, x.SupportDueAt,
        x.SupportStatus, x.ReturnedAt, x.ReturnVerifiedAt, x.ReturnVerifiedByUserId, x.VerificationReason,
        Attachments = x.Attachments.Select(a => new { a.Id, a.FileName, a.Size }).ToArray()
    });
}
