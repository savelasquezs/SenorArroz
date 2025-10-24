// SenorArroz.API/Controllers/BankPaymentsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using SenorArroz.Application.Features.BankPayments.Commands;
using SenorArroz.Application.Features.BankPayments.Queries;
using SenorArroz.Application.Features.BankPayments.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BankPaymentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public BankPaymentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Obtiene una lista paginada de pagos bancarios
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<BankPaymentDto>>> GetBankPayments(
        [FromQuery] int? orderId = null,
        [FromQuery] int? bankId = null,
        [FromQuery] int? branchId = null,
        [FromQuery] bool? verified = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string sortBy = "createdAt",
        [FromQuery] string sortOrder = "desc")
    {
        var query = new GetBankPaymentsQuery
        {
            OrderId = orderId,
            BankId = bankId,
            BranchId = branchId,
            Verified = verified,
            FromDate = fromDate,
            ToDate = toDate,
            Page = page,
            PageSize = pageSize,
            SortBy = sortBy,
            SortOrder = sortOrder
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Obtiene un pago bancario por ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<BankPaymentDto>> GetBankPayment(int id)
    {
        var query = new GetBankPaymentByIdQuery { Id = id };
        var result = await _mediator.Send(query);
        
        if (result == null)
            return NotFound();
            
        return Ok(result);
    }

    /// <summary>
    /// Obtiene todos los pagos bancarios no verificados
    /// </summary>
    [HttpGet("unverified")]
    public async Task<ActionResult<IEnumerable<BankPaymentDto>>> GetUnverifiedBankPayments()
    {
        var query = new GetUnverifiedBankPaymentsQuery();
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Crea un nuevo pago bancario
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<BankPaymentDto>> CreateBankPayment([FromBody] CreateBankPaymentDto createBankPaymentDto)
    {
        var command = new CreateBankPaymentCommand
        {
            OrderId = createBankPaymentDto.OrderId,
            BankId = createBankPaymentDto.BankId,
            Amount = createBankPaymentDto.Amount
        };

        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetBankPayment), new { id = result.Id }, result);
    }

    /// <summary>
    /// Actualiza el monto de un pago bancario
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,Superadmin,Cashier")]
    public async Task<ActionResult<BankPaymentDto>> UpdateBankPayment(int id, [FromBody] UpdateBankPaymentDto updateDto)
    {
        var command = new UpdateBankPaymentCommand
        {
            Id = id,
            Amount = updateDto.Amount
        };

        var result = await _mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Verifica un pago bancario (usa fecha automática del trigger de base de datos)
    /// </summary>
    [HttpPost("{id}/verify")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult> VerifyBankPayment(int id)
    {
        var command = new VerifyBankPaymentCommand
        {
            Id = id
        };

        var result = await _mediator.Send(command);
        
        if (!result)
            return NotFound();
            
        return Ok(new { message = "Pago bancario verificado exitosamente" });
    }

    /// <summary>
    /// Desverifica un pago bancario
    /// </summary>
    [HttpPost("{id}/unverify")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult> UnverifyBankPayment(int id)
    {
        var command = new UnverifyBankPaymentCommand { Id = id };
        var result = await _mediator.Send(command);
        
        if (!result)
            return NotFound();
            
        return Ok(new { message = "Pago bancario desverificado exitosamente" });
    }

    /// <summary>
    /// Elimina un pago bancario
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult> DeleteBankPayment(int id)
    {
        var command = new DeleteBankPaymentCommand { Id = id };
        var result = await _mediator.Send(command);
        
        if (!result)
            return NotFound();
            
        return NoContent();
    }
}
