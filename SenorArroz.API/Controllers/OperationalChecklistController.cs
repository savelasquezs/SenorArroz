using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Features.OperationalChecklist;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController, Route("api/operational-checklist"), Authorize(Roles = "Superadmin,Admin,Cashier")]
public sealed class OperationalChecklistController(IMediator mediator) : ControllerBase
{
    [HttpGet("today")]
    public Task<ChecklistDto> Today(CancellationToken ct) => mediator.Send(new GetChecklistQuery(), ct);

    [HttpGet("configuration"), Authorize(Roles = "Superadmin,Admin")]
    public Task<PagedResult<ReminderDefinitionDto>> Configuration([FromQuery] int page = 1,
        [FromQuery] int pageSize = 100, CancellationToken ct = default) =>
        mediator.Send(new GetReminderConfigurationQuery(page, pageSize), ct);

    [HttpPut("configuration"), Authorize(Roles = "Superadmin,Admin")]
    public Task<ActionResult<SaveReminderResult>> Save(SaveReminderDto body, CancellationToken ct) =>
        Change(new SaveReminderCommand(body), ct);

    [HttpPut("{id:int}/state")]
    public Task<ActionResult<ChecklistItemDto>> Check(int id, ChangeReminderStateDto body, CancellationToken ct) =>
        Change(new ChangeReminderStateCommand(id, body), ct);

    [HttpPost("sync-mintlify"), Authorize(Roles = "Superadmin,Admin")]
    public Task<ReminderSyncResult> Sync(CancellationToken ct) => mediator.Send(new SyncReminderDocumentationCommand(), ct);

    private async Task<ActionResult<T>> Change<T>(IRequest<T> command, CancellationToken ct)
    {
        try { return Ok(await mediator.Send(command, ct)); }
        catch (Exception e) when (e is ReminderConflictException or DbUpdateConcurrencyException)
        { return Conflict(new { code = "CHECKLIST_CHANGED", message = e is ReminderConflictException ? e.Message : "El recordatorio cambió. Actualiza e intenta de nuevo." }); }
    }
}
