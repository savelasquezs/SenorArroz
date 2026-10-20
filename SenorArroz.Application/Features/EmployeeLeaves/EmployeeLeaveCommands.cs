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

public sealed partial class EmployeeLeaveCommands : IRequestHandler<SaveEmployeeLeaveCommand, Guid>,
    IRequestHandler<ChangeEmployeeLeaveCommand, Guid>, IRequestHandler<MarkEmployeeLeaveReadCommand, Unit>,
    IRequestHandler<AddEmployeeLeaveAttachmentCommand, Guid>, IRequestHandler<DeleteEmployeeLeaveAttachmentCommand, Guid>,
    IRequestHandler<DownloadEmployeeLeaveAttachmentQuery, EmployeeLeaveFile>
{
    private readonly IApplicationDbContext db;
    private readonly ICurrentUser current;
    private readonly IPasswordService passwords;
    private readonly IClock clock;
    private readonly IConfiguration configuration;
    private readonly IEmployeeLeaveStorage storage;
    private readonly IBackgroundWorkSignal<EmailOutboxWork> signal;
    private readonly ILogger<EmployeeLeaveCommands> logger;

    public EmployeeLeaveCommands(IApplicationDbContext db, ICurrentUser current, IPasswordService passwords,
        IClock clock, IConfiguration configuration, IEmployeeLeaveStorage storage,
        IBackgroundWorkSignal<EmailOutboxWork> signal, ILogger<EmployeeLeaveCommands> logger)
    {
        this.db = db; this.current = current; this.passwords = passwords; this.clock = clock;
        this.configuration = configuration; this.storage = storage; this.signal = signal; this.logger = logger;
    }

    public async Task<Guid> Handle(ChangeEmployeeLeaveCommand command, CancellationToken ct)
    {
        var input = command.Input;
        User actor;
        try { actor = await ConfirmAsync(input.Password, ct); }
        finally { input.Password = ""; }
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
        var periodRecord = leave.PolicyVersion >= EmployeeLeaveRules.CurrentPolicyVersion && EmployeeLeaveRules.Kind(leave.Kind).IsPeriodRecord;
        var reason = command.Action == "return" ? "Regreso confirmado por el trabajador."
            : EmployeeLeaveRules.RequiredText(input.Reason, 1000, "Motivo");
        switch (command.Action)
        {
            case "decision":
                if (!review || leave.Status is not ("pending" or "postponed")) throw new UnauthorizedAccessException();
                EmployeeLeaveRules.ValidateDecision(input.Status ?? "", leave.Kind, input.IsPaid);
                if (input.Status == "approved")
                {
                    EmployeeLeaveRules.RequirePriorEvidence(leave);
                    if (EmployeeLeaveRules.EvidenceStates(leave).Any(x => x.Required && x.Stage == "before" && x.Status != "verified"))
                        throw new BusinessException("Verifica los soportes previos antes de aprobar. Los documentos posteriores pueden quedar pendientes.");
                    if (input.SupportDueAt is { Kind: not DateTimeKind.Utc } || input.SupportDueAt.HasValue && input.SupportDueAt < clock.UtcNow)
                        throw new BusinessException("El plazo para presentar soporte debe ser una fecha UTC vigente.");
                    if (!periodRecord && await db.EmployeeLeaveRequests.AnyAsync(x => x.UserId == leave.UserId
                        && x.Id != leave.Id && (x.Kind != "lactation" || x.PolicyVersion < 2) && x.Status == "approved"
                        && x.StartsAt < leave.EndsAt && x.EndsAt > leave.StartsAt, ct))
                        throw new BusinessException("El trabajador ya tiene un permiso aprobado que se cruza con este intervalo.");
                    leave.SupportRequired |= input.SupportRequired;
                    leave.SupportDueAt = leave.SupportRequired ? input.SupportDueAt : null;
                    EmployeeLeaveRules.RefreshSupport(leave);
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
                if (!own || periodRecord || leave.Status != "approved" || leave.ReturnedAt.HasValue || leave.StartsAt > clock.UtcNow)
                    throw new BusinessException("No se puede registrar el regreso de este permiso.");
                leave.ReturnedAt = clock.UtcNow;
                break;
            case "verify":
                if (!review || leave.Status is not ("pending" or "postponed" or "approved")) throw new UnauthorizedAccessException();
                if (input.SupportStatus is not ("verified" or "rejected" or null)) throw new BusinessException("Verificación de soporte no válida.");
                if (input.SupportStatus is not null)
                {
                    var activeFiles = leave.Attachments.Where(x => x.SupersededAt == null).ToArray();
                    var file = input.AttachmentId.HasValue ? activeFiles.SingleOrDefault(x => x.Id == input.AttachmentId)
                        : activeFiles.Length == 1 ? activeFiles[0] : null;
                    if (file is null) throw new BusinessException("Selecciona el documento específico que deseas revisar.");
                    file.ReviewStatus = input.SupportStatus;
                    file.ReviewReason = reason;
                    file.ReviewedAt = clock.UtcNow;
                    file.ReviewedByUserId = actor.Id;
                    file.UpdatedAt = clock.UtcNow;
                    EmployeeLeaveRules.RefreshSupport(leave);
                }
                if (input.VerifyReturn)
                {
                    if (periodRecord || leave.Status != "approved" || !leave.ReturnedAt.HasValue)
                        throw new BusinessException("El trabajador aún no ha registrado el regreso de un permiso aprobado.");
                    leave.ReturnVerifiedAt = clock.UtcNow;
                    leave.ReturnVerifiedByUserId = actor.Id;
                }
                if (input.SupportStatus is null && !input.VerifyReturn) throw new BusinessException("Selecciona qué deseas verificar.");
                leave.VerificationReason = reason;
                break;
            default: throw new BusinessException("Acción no válida.");
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

    private async Task<User> ConfirmAsync(string password, CancellationToken ct)
    {
        var actor = await EmployeeLeaveQueries.ActorAsync(db, current, ct);
        EmployeePasswordConfirmation.Verify(actor, password, passwords);
        return actor;
    }
    private async Task<EmployeeLeaveRequest> LoadAsync(Guid publicId, CancellationToken ct) =>
        await db.EmployeeLeaveRequests.Include(x => x.Attachments).Include(x => x.Branch).SingleOrDefaultAsync(x => x.PublicId == publicId, ct)
        ?? throw new NotFoundException("Permiso no encontrado.");
    private async Task<IDbContextTransaction?> BeginAsync(int tenantId, int userId, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return null;
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
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
        db.EntityAuditLogs.Add(new EntityAuditLog { TenantId = leave.TenantId, BranchId = leave.BranchId,
            EntityType = "employee_leave_request", EntityId = leave.Id, OperationType = operation, ChangedAt = clock.UtcNow,
            BusinessDate = clock.UtcNow.AddHours(-5).Date, ChangedByUserId = actor.Id, ChangedByNameSnapshot = actor.Name,
            SummaryText = reason, BeforeJson = before, AfterJson = Snapshot(leave),
            MetadataJson = "{\"confirmation\":\"current_password\",\"channel\":\"application\"}" });
        await EmployeeLeaveEmail.EnqueueAsync(db, configuration, leave, actor.Id, operation, clock.UtcNow, ct);
        await db.SaveChangesAsync(ct);
    }
    private async Task DeleteStorageBestEffortAsync(string key)
    {
        try { using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await storage.DeleteAsync(key, cleanup.Token); }
        catch (Exception ex) { logger.LogWarning(ex, "Private employee attachment cleanup pending for {StorageKey}.", key); }
    }
    private static string Snapshot(EmployeeLeaveRequest x) => JsonSerializer.Serialize(new
    {
        x.PublicId, x.Version, x.Kind, x.Status, x.StartsAt, x.EndsAt, x.IsAllDay, x.RequestedIsPaid, x.PolicyVersion,
        x.Justification, x.DocumentNumber, x.IsPaid, x.DecisionReason, x.DecidedByUserId, x.DecidedAt,
        x.SupportRequired, x.SupportDueAt, x.SupportStatus, x.ReturnedAt, x.ReturnVerifiedAt, x.ReturnVerifiedByUserId, x.VerificationReason,
        Attachments = x.Attachments.Select(a => new { a.Id, a.FileName, a.Size, a.EvidenceCode, a.ReviewStatus,
            a.ReviewReason, a.ReviewedAt, a.ReviewedByUserId, a.ReplacesAttachmentId, a.SupersededAt }).ToArray()
    });
}
