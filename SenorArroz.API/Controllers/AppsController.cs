// SenorArroz.API/Controllers/AppsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using SenorArroz.Application.Features.Apps.Commands;
using SenorArroz.Application.Features.Apps.Queries;
using SenorArroz.Application.Features.Apps.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AppsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AppsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Obtiene una lista paginada de apps
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<AppDto>>> GetApps(
        [FromQuery] int? bankId = null,
        [FromQuery] string? name = null,
        [FromQuery] int? branchId = null,
        [FromQuery] bool? active = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string sortBy = "name",
        [FromQuery] string sortOrder = "asc")
    {
        var query = new GetAppsQuery
        {
            BankId = bankId,
            Name = name,
            BranchId = branchId,
            Active = active,
            Page = page,
            PageSize = pageSize,
            SortBy = sortBy,
            SortOrder = sortOrder
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Obtiene una app por ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<AppDto>> GetApp(int id)
    {
        var query = new GetAppByIdQuery { Id = id };
        var result = await _mediator.Send(query);
        
        if (result == null)
            return NotFound();
            
        return Ok(result);
    }

    /// <summary>
    /// Obtiene todas las apps de un banco específico
    /// </summary>
    [HttpGet("by-bank/{bankId}")]
    public async Task<ActionResult<IEnumerable<AppDto>>> GetAppsByBank(int bankId)
    {
        var query = new GetAppsByBankQuery { BankId = bankId };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Crea una nueva app
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<AppDto>> CreateApp([FromBody] CreateAppDto createAppDto)
    {
        var command = new CreateAppCommand
        {
            BankId = createAppDto.BankId,
            Name = createAppDto.Name,
            ImageUrl = createAppDto.ImageUrl,
            Active = createAppDto.Active
        };

        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetApp), new { id = result.Id }, result);
    }

    /// <summary>
    /// Actualiza una app existente
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<AppDto>> UpdateApp(int id, [FromBody] UpdateAppDto updateAppDto)
    {
        var command = new UpdateAppCommand
        {
            Id = id,
            BankId = updateAppDto.BankId,
            Name = updateAppDto.Name,
            ImageUrl = updateAppDto.ImageUrl,
            Active = updateAppDto.Active
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Elimina una app (soft delete)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult> DeleteApp(int id)
    {
        var command = new DeleteAppCommand { Id = id };
        var result = await _mediator.Send(command);
        
        if (!result)
            return NotFound();
            
        return NoContent();
    }
}
