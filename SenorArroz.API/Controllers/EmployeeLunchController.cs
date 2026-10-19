using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SenorArroz.Application.Features.EmployeeLunches;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController, Authorize, Route("api/employee-lunch")]
public sealed class EmployeeLunchController(IMediator mediator) : ControllerBase
{
    [HttpGet("me")]
    public Task<EmployeeLunchStateDto> Me(CancellationToken ct) => mediator.Send(new GetEmployeeLunchQuery(), ct);

    [HttpGet("branch"), Authorize(Roles = "Superadmin,Admin,Cashier,Kitchen")]
    public Task<PagedResult<EmployeeLunchStateDto>> Branch([FromQuery] DateOnly? date,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        mediator.Send(new GetBranchLunchesQuery(date, page, pageSize), ct);

    [HttpPost("start"), EnableRateLimiting("auth-sensitive")]
    public Task<EmployeeLunchDto> Start(LunchAction body, CancellationToken ct) =>
        mediator.Send(new ChangeEmployeeLunchCommand(false, body.Password, body.UserId), ct);

    [HttpPost("finish"), EnableRateLimiting("auth-sensitive")]
    public Task<EmployeeLunchDto> Finish(LunchAction body, CancellationToken ct) =>
        mediator.Send(new ChangeEmployeeLunchCommand(true, body.Password, body.UserId, body.LunchId), ct);

    [HttpPatch("{id:int}"), Authorize(Roles = "Superadmin,Admin")]
    public Task<EmployeeLunchDto> Correct(int id, LunchCorrection body, CancellationToken ct) =>
        mediator.Send(new CorrectEmployeeLunchCommand(id, body.StartedAt, body.EndedAt, body.ExpectedUpdatedAt, body.Reason), ct);
}

public sealed record LunchAction(string Password, int? UserId, int? LunchId);
public sealed record LunchCorrection(DateTime StartedAt, DateTime? EndedAt, DateTime ExpectedUpdatedAt, string Reason);
