using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SenorArroz.API.Hubs;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.EmployeeLeaves;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Services;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController, Authorize, Route("api/employee-leaves")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EmployeeLeavesController(IMediator mediator, IApplicationDbContext db, ICurrentUser current,
    IHubContext<OrderHub> hub, IFcmPushService push, ILogger<EmployeeLeavesController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions SubmissionJson = new() { PropertyNameCaseInsensitive = true, MaxDepth = 8 };
    [HttpGet("catalog")]
    public async Task<IReadOnlyList<EmployeeLeaveKind>> Catalog(CancellationToken ct)
    {
        await EmployeeLeaveQueries.ActorAsync(db, current, ct);
        return EmployeeLeaveRules.Kinds;
    }
    [HttpGet]
    public Task<PagedResult<EmployeeLeaveListDto>> List([FromQuery] GetEmployeeLeavesQuery query, CancellationToken ct) => mediator.Send(query, ct);
    [HttpGet("summary")]
    public Task<EmployeeLeaveSummaryDto> Summary(CancellationToken ct) => mediator.Send(new GetEmployeeLeaveSummaryQuery(), ct);
    [HttpGet("{publicId:guid}")]
    public Task<EmployeeLeaveDetailDto> Detail(Guid publicId, CancellationToken ct) => mediator.Send(new GetEmployeeLeaveQuery(publicId), ct);
    [HttpGet("{publicId:guid}/history")]
    public Task<PagedResult<EmployeeLeaveHistoryDto>> History(Guid publicId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken ct = default) => mediator.Send(new GetEmployeeLeaveHistoryQuery(publicId, page, pageSize), ct);

    // Existing JSON clients still use the SAME server rules; omitting files never bypasses prior-evidence requirements.
    [HttpPost, EnableRateLimiting("auth-sensitive")]
    public Task<IActionResult> Create(EmployeeLeaveInput input, CancellationToken ct) => Mutate(() => mediator.Send(new SaveEmployeeLeaveCommand(null, input), ct));
    [HttpPut("{publicId:guid}"), EnableRateLimiting("auth-sensitive")]
    public Task<IActionResult> Update(Guid publicId, EmployeeLeaveInput input, CancellationToken ct) => Mutate(() => mediator.Send(new SaveEmployeeLeaveCommand(publicId, input), ct));

    [HttpPost("submit"), Consumes("multipart/form-data"), EnableRateLimiting("auth-sensitive")]
    [RequestSizeLimit(26 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 26 * 1024 * 1024, ValueLengthLimit = 16384, ValueCountLimit = 64)]
    public Task<IActionResult> Submit([FromForm] EmployeeLeaveSubmissionForm form, CancellationToken ct) => Mutate(async () =>
    {
        EmployeeLeaveInput? input = null;
        try
        {
            if (form.Payload.Length > 16384 || form.Evidence.Length > 4096 || form.Files.Count > EmployeeLeaveFiles.MaxFiles)
                throw new BusinessException("La solicitud supera los límites permitidos.");
            EmployeeLeaveFileMetadata[] metadata;
            try
            {
                input = JsonSerializer.Deserialize<EmployeeLeaveInput>(form.Payload, SubmissionJson)
                    ?? throw new JsonException();
                metadata = JsonSerializer.Deserialize<EmployeeLeaveFileMetadata[]>(form.Evidence, SubmissionJson) ?? [];
            }
            catch (JsonException) { throw new BusinessException("Los datos de la solicitud o de los soportes no son válidos."); }
            input.Password = form.Password;
            if (metadata.Length != form.Files.Count) throw new BusinessException("Cada archivo debe indicar qué requisito acredita.");
            var files = new List<EmployeeLeaveNewFile>();
            for (var i = 0; i < form.Files.Count; i++)
                files.Add(new() { UploadKey = metadata[i].UploadKey, EvidenceCode = metadata[i].EvidenceCode,
                    FileName = form.Files[i].FileName, ContentType = form.Files[i].ContentType,
                    Content = await ReadFile(form.Files[i], ct) });
            return await mediator.Send(new SaveEmployeeLeaveCommand(form.PublicId, input, files), ct);
        }
        finally { form.Password = ""; form.Payload = ""; if (input is not null) input.Password = ""; }
    });

    [HttpPost("{publicId:guid}/decision"), Authorize(Roles = "Superadmin,Admin"), EnableRateLimiting("auth-sensitive")]
    public Task<IActionResult> Decide(Guid publicId, EmployeeLeaveAction input, CancellationToken ct) => Mutate(() => mediator.Send(new ChangeEmployeeLeaveCommand(publicId, "decision", input), ct));
    [HttpPost("{publicId:guid}/cancel"), EnableRateLimiting("auth-sensitive")]
    public Task<IActionResult> Cancel(Guid publicId, EmployeeLeaveAction input, CancellationToken ct) => Mutate(() => mediator.Send(new ChangeEmployeeLeaveCommand(publicId, "cancel", input), ct));
    [HttpPost("{publicId:guid}/return"), EnableRateLimiting("auth-sensitive")]
    public Task<IActionResult> Return(Guid publicId, EmployeeLeaveAction input, CancellationToken ct) => Mutate(() => mediator.Send(new ChangeEmployeeLeaveCommand(publicId, "return", input), ct));
    [HttpPost("{publicId:guid}/verify"), Authorize(Roles = "Superadmin,Admin"), EnableRateLimiting("auth-sensitive")]
    public Task<IActionResult> Verify(Guid publicId, EmployeeLeaveAction input, CancellationToken ct) => Mutate(() => mediator.Send(new ChangeEmployeeLeaveCommand(publicId, "verify", input), ct));
    [HttpPost("{publicId:guid}/read")]
    public async Task<IActionResult> Read(Guid publicId, EmployeeLeaveRead input, CancellationToken ct)
    {
        try { await mediator.Send(new MarkEmployeeLeaveReadCommand(publicId, input.Version), ct); return NoContent(); }
        catch (Exception ex) when (IsConflict(ex)) { return Changed(); }
    }
    [HttpPost("{publicId:guid}/attachments"), EnableRateLimiting("auth-sensitive")]
    [RequestSizeLimit(6 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public Task<IActionResult> Upload(Guid publicId, [FromForm] EmployeeLeaveUploadForm form, CancellationToken ct) => Mutate(async () =>
    {
        try
        {
            return await mediator.Send(new AddEmployeeLeaveAttachmentCommand(publicId, new EmployeeLeaveUpload
            {
                Version = form.Version, Password = form.Password, UploadKey = form.UploadKey,
                FileName = form.File?.FileName ?? "", ContentType = form.File?.ContentType ?? "",
                Content = await ReadFile(form.File, ct), EvidenceCode = form.EvidenceCode, ReplacesAttachmentId = form.ReplacesAttachmentId
            }), ct);
        }
        finally { form.Password = ""; }
    });
    [HttpDelete("{publicId:guid}/attachments/{attachmentId:int}"), EnableRateLimiting("auth-sensitive")]
    public Task<IActionResult> DeleteAttachment(Guid publicId, int attachmentId, [FromBody] EmployeeLeaveAction input, CancellationToken ct) =>
        Mutate(() => mediator.Send(new DeleteEmployeeLeaveAttachmentCommand(publicId, attachmentId, input), ct));
    [HttpGet("{publicId:guid}/attachments/{attachmentId:int}")]
    public async Task<IActionResult> Download(Guid publicId, int attachmentId, CancellationToken ct)
    {
        var file = await mediator.Send(new DownloadEmployeeLeaveAttachmentQuery(publicId, attachmentId), ct);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Content, file.ContentType, file.FileName, enableRangeProcessing: false);
    }
    private static async Task<byte[]> ReadFile(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length <= 0 || file.Length > EmployeeLeaveFiles.MaxBytes)
            throw new BusinessException("Adjunta un soporte PDF, JPG o PNG de máximo 5 MB.");
        using var stream = new MemoryStream((int)file.Length);
        await file.CopyToAsync(stream, ct);
        return stream.ToArray();
    }
    private async Task<IActionResult> Mutate(Func<Task<Guid>> operation)
    {
        try { var publicId = await operation(); await NotifyAsync(publicId); return Ok(new { publicId }); }
        catch (Exception ex) when (IsConflict(ex)) { return Changed(); }
    }
    private ObjectResult Changed() => StatusCode(409, new { code = "EMPLOYEE_LEAVE_CONFLICT", message = "La solicitud cambió. Actualiza el detalle y confirma nuevamente." });
    private static bool IsConflict(Exception error) => error is EmployeeLeaveConflictException or DbUpdateConcurrencyException
        || error is DbUpdateException { InnerException: PostgresException { SqlState: "23505" or "40001" or "40P01" } };
    private async Task NotifyAsync(Guid publicId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var ct = timeout.Token;
            var leave = await db.EmployeeLeaveRequests.AsNoTracking().SingleAsync(x => x.PublicId == publicId, ct);
            var ids = await EmployeeLeaveQueries.Reviewers(db, leave).Select(x => x.Id).ToListAsync(ct);
            ids.Add(leave.UserId);
            await hub.Clients.Groups(ids.Distinct().Select(id => EmployeeLeaveRealtime.UserGroup(leave.TenantId, id)).ToList())
                .SendAsync("EmployeeLeaveChanged", new { publicId, version = leave.Version }, ct);
            if (current.Id == leave.UserId) return;
            var tokens = await db.UserDeviceTokens.Where(x => x.UserId == leave.UserId).Select(x => x.Token).Distinct().ToListAsync(ct);
            await push.SendToTokensAsync(tokens, "Permisos laborales", "Tu solicitud tiene una actualización. Abre la aplicación para consultarla.",
                new Dictionary<string, string> { ["type"] = "employee_leave_changed", ["requestId"] = publicId.ToString("D"),
                    ["userId"] = leave.UserId.ToString(), ["version"] = leave.Version.ToString() }, ct,
                correlationId: $"leave-{leave.Id}-{leave.Version}", androidChannelId: "employee_leaves");
        }
        catch (Exception ex) { logger.LogWarning(ex, "Permiso guardado; aviso inmediato no disponible para {PublicId}.", publicId); }
    }
}
public sealed record EmployeeLeaveRead(long Version);
public sealed record EmployeeLeaveFileMetadata(Guid UploadKey, string EvidenceCode);
public sealed class EmployeeLeaveSubmissionForm
{
    public Guid? PublicId { get; set; }
    public string Payload { get; set; } = "";
    public string Evidence { get; set; } = "[]";
    public string Password { get; set; } = "";
    public List<IFormFile> Files { get; set; } = [];
}
public sealed class EmployeeLeaveUploadForm
{
    public long Version { get; set; }
    public Guid UploadKey { get; set; }
    public string Password { get; set; } = "";
    public IFormFile? File { get; set; }
    public string EvidenceCode { get; set; } = "additional";
    public int? ReplacesAttachmentId { get; set; }
}
