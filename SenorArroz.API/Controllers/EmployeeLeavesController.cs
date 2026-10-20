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
using SenorArroz.Domain.Services;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController, Authorize, Route("api/employee-leaves")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EmployeeLeavesController(IMediator mediator, IApplicationDbContext db, ICurrentUser current,
    IHubContext<OrderHub> hub, IFcmPushService push, ILogger<EmployeeLeavesController> logger) : ControllerBase
{
    [HttpGet("catalog")]
    public async Task<IReadOnlyList<EmployeeLeaveKind>> Catalog(CancellationToken ct)
    {
        await EmployeeLeaveQueries.ActorAsync(db, current, ct);
        return EmployeeLeaveRules.Kinds;
    }

    [HttpGet]
    public Task<PagedResult<EmployeeLeaveListDto>> List([FromQuery] GetEmployeeLeavesQuery query, CancellationToken ct) =>
        mediator.Send(query, ct);

    [HttpGet("summary")]
    public Task<EmployeeLeaveSummaryDto> Summary(CancellationToken ct) => mediator.Send(new GetEmployeeLeaveSummaryQuery(), ct);

    [HttpGet("{publicId:guid}")]
    public Task<EmployeeLeaveDetailDto> Detail(Guid publicId, CancellationToken ct) => mediator.Send(new GetEmployeeLeaveQuery(publicId), ct);

    [HttpGet("{publicId:guid}/history")]
    public Task<PagedResult<EmployeeLeaveHistoryDto>> History(Guid publicId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        mediator.Send(new GetEmployeeLeaveHistoryQuery(publicId, page, pageSize), ct);

    [HttpPost, EnableRateLimiting("auth-sensitive")]
    public Task<IActionResult> Create(EmployeeLeaveInput input, CancellationToken ct) =>
        Mutate(() => mediator.Send(new SaveEmployeeLeaveCommand(null, input), ct));

    [HttpPut("{publicId:guid}"), EnableRateLimiting("auth-sensitive")]
    public Task<IActionResult> Update(Guid publicId, EmployeeLeaveInput input, CancellationToken ct) =>
        Mutate(() => mediator.Send(new SaveEmployeeLeaveCommand(publicId, input), ct));

    [HttpPost("{publicId:guid}/decision"), Authorize(Roles = "Superadmin,Admin"), EnableRateLimiting("auth-sensitive")]
    public Task<IActionResult> Decide(Guid publicId, EmployeeLeaveAction input, CancellationToken ct) =>
        Mutate(() => mediator.Send(new ChangeEmployeeLeaveCommand(publicId, "decision", input), ct));

    [HttpPost("{publicId:guid}/cancel"), EnableRateLimiting("auth-sensitive")]
    public Task<IActionResult> Cancel(Guid publicId, EmployeeLeaveAction input, CancellationToken ct) =>
        Mutate(() => mediator.Send(new ChangeEmployeeLeaveCommand(publicId, "cancel", input), ct));

    [HttpPost("{publicId:guid}/return"), EnableRateLimiting("auth-sensitive")]
    public Task<IActionResult> Return(Guid publicId, EmployeeLeaveAction input, CancellationToken ct) =>
        Mutate(() => mediator.Send(new ChangeEmployeeLeaveCommand(publicId, "return", input), ct));

    [HttpPost("{publicId:guid}/verify"), Authorize(Roles = "Superadmin,Admin"), EnableRateLimiting("auth-sensitive")]
    public Task<IActionResult> Verify(Guid publicId, EmployeeLeaveAction input, CancellationToken ct) =>
        Mutate(() => mediator.Send(new ChangeEmployeeLeaveCommand(publicId, "verify", input), ct));

    [HttpPost("{publicId:guid}/read")]
    public async Task<IActionResult> Read(Guid publicId, EmployeeLeaveRead input, CancellationToken ct)
    {
        try { await mediator.Send(new MarkEmployeeLeaveReadCommand(publicId, input.Version), ct); return NoContent(); }
        catch (Exception ex) when (IsConflict(ex)) { return Changed(); }
    }

    [HttpPost("{publicId:guid}/attachments"), EnableRateLimiting("auth-sensitive")]
    [RequestSizeLimit(6 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public Task<IActionResult> Upload(Guid publicId, [FromForm] EmployeeLeaveUploadForm form, CancellationToken ct) =>
        Mutate(async () =>
        {
            try
            {
                if (form.File is null || form.File.Length == 0 || form.File.Length > EmployeeLeaveFiles.MaxBytes)
                    throw new SenorArroz.Domain.Exceptions.BusinessException("Adjunta un soporte de máximo 5 MB.");
                using var stream = new MemoryStream((int)form.File.Length);
                await form.File.CopyToAsync(stream, ct);
                return await mediator.Send(new AddEmployeeLeaveAttachmentCommand(publicId, new EmployeeLeaveUpload
                {
                    Version = form.Version, Password = form.Password, UploadKey = form.UploadKey,
                    FileName = form.File.FileName, ContentType = form.File.ContentType, Content = stream.ToArray()
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

    private async Task<IActionResult> Mutate(Func<Task<Guid>> operation)
    {
        try
        {
            var publicId = await operation();
            // Los correos ya están persistidos. Un fallo de push/SignalR no convierte un guardado en error.
            await NotifyAsync(publicId);
            return Ok(new { publicId });
        }
        catch (Exception ex) when (IsConflict(ex)) { return Changed(); }
    }

    private ObjectResult Changed() => StatusCode(409, new { code = "EMPLOYEE_LEAVE_CONFLICT",
        message = "La solicitud cambió. Actualiza el detalle y confirma nuevamente." });

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
public sealed class EmployeeLeaveUploadForm
{
    public long Version { get; set; }
    public Guid UploadKey { get; set; }
    public string Password { get; set; } = "";
    public IFormFile? File { get; set; }
}
