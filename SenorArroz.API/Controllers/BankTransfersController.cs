using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using SenorArroz.Application.Features.BankTransfers.Commands;
using SenorArroz.Application.Features.BankTransfers.Queries;
using SenorArroz.Application.Features.BankTransfers.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Superadmin,Cashier")]
public class BankTransfersController : ControllerBase
{
    private readonly IMediator _mediator;

    public BankTransfersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Obtiene una lista paginada de transferencias entre bancos
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<BankTransferDto>>> GetBankTransfers(
        [FromQuery] int? branchId = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int? fromBankId = null,
        [FromQuery] int? toBankId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string sortBy = "createdAt",
        [FromQuery] string sortOrder = "desc")
    {
        var query = new GetBankTransfersQuery
        {
            BranchId = branchId,
            FromDate = fromDate,
            ToDate = toDate,
            FromBankId = fromBankId,
            ToBankId = toBankId,
            Page = page,
            PageSize = pageSize,
            SortBy = sortBy,
            SortOrder = sortOrder
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Crea una nueva transferencia entre bancos
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<BankTransferDto>> CreateBankTransfer([FromBody] CreateBankTransferDto dto)
    {
        var command = new CreateBankTransferCommand
        {
            FromBankId = dto.FromBankId is > 0 ? dto.FromBankId : null,
            ToBankId = dto.ToBankId is > 0 ? dto.ToBankId : null,
            Amount = dto.Amount,
            Note = dto.Note
        };

        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetBankTransfers), new { id = result.Id }, result);
    }
}
