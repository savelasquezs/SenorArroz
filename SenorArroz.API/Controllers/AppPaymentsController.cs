// SenorArroz.API/Controllers/AppPaymentsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using SenorArroz.Application.Features.AppPayments.Commands;
using SenorArroz.Application.Features.AppPayments.Queries;
using SenorArroz.Application.Features.AppPayments.DTOs;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AppPaymentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AppPaymentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Obtiene una lista paginada de pagos de apps
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<AppPaymentDto>>> GetAppPayments(
        [FromQuery] int? orderId = null,
        [FromQuery] int? appId = null,
        [FromQuery] int? bankId = null,
        [FromQuery] int? branchId = null,
        [FromQuery] bool? settled = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string sortBy = "createdAt",
        [FromQuery] string sortOrder = "desc")
    {
        var query = new GetAppPaymentsQuery
        {
            OrderId = orderId,
            AppId = appId,
            BankId = bankId,
            BranchId = branchId,
            Settled = settled,
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
    /// Obtiene un pago de app por ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<AppPaymentDto>> GetAppPayment(int id)
    {
        var query = new GetAppPaymentByIdQuery { Id = id };
        var result = await _mediator.Send(query);
        
        if (result == null)
            return NotFound();
            
        return Ok(result);
    }

    /// <summary>
    /// Obtiene todos los pagos de apps no liquidados
    /// </summary>
    [HttpGet("unsettled")]
    public async Task<ActionResult<IEnumerable<AppPaymentDto>>> GetUnsettledAppPayments(
        [FromQuery] int? appId = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var query = new GetUnsettledAppPaymentsQuery
        {
            AppId = appId,
            FromDate = fromDate,
            ToDate = toDate,
            Page = page,
            PageSize = pageSize
        };

        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Crea un nuevo pago de app
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult<AppPaymentDto>> CreateAppPayment([FromBody] CreateAppPaymentDto createAppPaymentDto)
    {
        var command = new CreateAppPaymentCommand
        {
            OrderId = createAppPaymentDto.OrderId,
            AppId = createAppPaymentDto.AppId,
            Amount = createAppPaymentDto.Amount
        };

        var result = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetAppPayment), new { id = result.Id }, result);
    }

    /// <summary>
    /// Liquida un pago de app (marca como settled y crea bank payment)
    /// </summary>
    [HttpPost("{id}/settle")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult> SettleAppPayment(int id)
    {
        var command = new SettleAppPaymentCommand { Id = id };
        var result = await _mediator.Send(command);
        
        if (!result)
            return NotFound();
            
        return Ok(new { message = "Pago de app liquidado exitosamente. Se creó el bank payment correspondiente." });
    }

    /// <summary>
    /// Liquida múltiples pagos de apps (marca como settled y crea un solo bank payment)
    /// </summary>
    [HttpPost("settle-multiple")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult> SettleMultipleAppPayments([FromBody] SettleAppPaymentsDto settleDto)
    {
        var command = new SettleMultipleAppPaymentsCommand
        {
            PaymentIds = settleDto.PaymentIds
        };

        var result = await _mediator.Send(command);
        
        if (!result)
            return BadRequest();
            
        return Ok(new { message = "Pagos de apps liquidados exitosamente. Se creó un bank payment con el total." });
    }

    /// <summary>
    /// Desliquida un pago de app (marca como unsettled y elimina bank payment)
    /// </summary>
    [HttpPost("{id}/unsettle")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult> UnsettleAppPayment(int id)
    {
        var command = new UnsettleAppPaymentCommand { Id = id };
        var result = await _mediator.Send(command);
        
        if (!result)
            return NotFound();
            
        return Ok(new { message = "Pago de app desliquidado exitosamente. Se eliminó el bank payment correspondiente." });
    }

    /// <summary>
    /// Elimina un pago de app
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Superadmin")]
    public async Task<ActionResult> DeleteAppPayment(int id)
    {
        var command = new DeleteAppPaymentCommand { Id = id };
        var result = await _mediator.Send(command);
        
        if (!result)
            return NotFound();
            
        return NoContent();
    }
}
