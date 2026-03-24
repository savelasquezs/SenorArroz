// SenorArroz.API/Controllers/BanksController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using SenorArroz.Application.Features.Banks.Commands;
using SenorArroz.Application.Features.Banks.Queries;
using SenorArroz.Application.Features.Banks.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BanksController : ControllerBase
{
    private readonly IMediator _mediator;

    public BanksController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Obtiene una lista paginada de bancos
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<BankDto>>> GetBanks(
        [FromQuery] int? branchId = null,
        [FromQuery] string? name = null,
        [FromQuery] bool? active = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string sortBy = "name",
        [FromQuery] string sortOrder = "asc")
    {
        var query = new GetBanksQuery
        {
            BranchId = branchId,
            Name = name,
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
    /// Obtiene un banco por ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<BankDto>> GetBank(int id)
    {
        var query = new GetBankByIdQuery { Id = id };
        var result = await _mediator.Send(query);
        
        if (result == null)
            return NotFound();
            
        return Ok(result);
    }

    /// <summary>
    /// Obtiene detalles completos de un banco con estadísticas
    /// </summary>
    [HttpGet("{id}/detail")]
    public async Task<ActionResult<BankDetailDto>> GetBankDetail(int id)
    {
        var query = new GetBankDetailQuery { Id = id };
        var result = await _mediator.Send(query);
        
        if (result == null)
            return NotFound();
            
        return Ok(result);
    }

    /// <summary>
    /// Desglose de movimiento neto del banco en un rango de fechas (calendario Colombia).
    /// </summary>
    [HttpGet("{id}/movements/period-summary")]
    public async Task<ActionResult<BankBalanceBreakdownDto>> GetBankLedgerPeriod(
        int id,
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate)
    {
        var result = await _mediator.Send(new GetBankLedgerPeriodQuery
        {
            BankId = id,
            FromDate = fromDate,
            ToDate = toDate
        });

        if (result == null)
            return NotFound();

        return Ok(result);
    }

    /// <summary>
    /// Pagos de gastos imputados al banco en el período (paginado).
    /// </summary>
    [HttpGet("{id}/movements/expense-payments")]
    public async Task<ActionResult<PagedResult<ExpenseBankPaymentLineDto>>> GetBankExpenseBankPaymentsPaged(
        int id,
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate,
        [FromQuery] int? branchId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new GetBankExpenseBankPaymentsPagedQuery
        {
            BankId = id,
            FromDate = fromDate,
            ToDate = toDate,
            BranchId = branchId,
            Page = page,
            PageSize = pageSize
        });

        if (result == null)
            return NotFound();

        return Ok(result);
    }

    /// <summary>
    /// Abonos a domiciliarios por transferencia al banco en el período (paginado).
    /// </summary>
    [HttpGet("{id}/movements/deliveryman-transfers")]
    public async Task<ActionResult<PagedResult<DeliverymanBankAdvanceLineDto>>> GetBankDeliverymanTransferAdvancesPaged(
        int id,
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate,
        [FromQuery] int? branchId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _mediator.Send(new GetBankDeliverymanTransferAdvancesPagedQuery
        {
            BankId = id,
            FromDate = fromDate,
            ToDate = toDate,
            BranchId = branchId,
            Page = page,
            PageSize = pageSize
        });

        if (result == null)
            return NotFound();

        return Ok(result);
    }

    /// <summary>
    /// Crea un nuevo banco
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<BankDto>> CreateBank([FromBody] CreateBankDto createBankDto)
    {
        var command = new CreateBankCommand
        {
            BranchId = createBankDto.BranchId,
            Name = createBankDto.Name,
            ImageUrl = createBankDto.ImageUrl,
            Active = createBankDto.Active
        };

        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetBank), new { id = result.Id }, result);
    }

    /// <summary>
    /// Actualiza un banco existente
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<BankDto>> UpdateBank(int id, [FromBody] UpdateBankDto updateBankDto)
    {
        var command = new UpdateBankCommand
        {
            Id = id,
            Name = updateBankDto.Name,
            ImageUrl = updateBankDto.ImageUrl,
            Active = updateBankDto.Active
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Elimina un banco (soft delete)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult> DeleteBank(int id)
    {
        var command = new DeleteBankCommand { Id = id };
        var result = await _mediator.Send(command);
        
        if (!result)
            return NotFound();
            
        return NoContent();
    }
}
