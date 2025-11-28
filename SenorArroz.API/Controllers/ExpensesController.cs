// SenorArroz.API/Controllers/ExpensesController.cs
using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Features.Expenses.Commands;
using SenorArroz.Application.Features.Expenses.DTOs;
using SenorArroz.Application.Features.Expenses.Queries;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ExpensesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IMapper _mapper;

    public ExpensesController(IMediator mediator, IMapper mapper)
    {
        _mediator = mediator;
        _mapper = mapper;
    }

    /// <summary>
    /// Obtener gastos con paginación y filtros
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<ExpenseDto>>>> GetExpenses(
        [FromQuery] ExpenseSearchDto searchDto)
    {
        var query = _mapper.Map<GetExpensesQuery>(searchDto);
        var result = await _mediator.Send(query);
        return Ok(ApiResponse<PagedResult<ExpenseDto>>.SuccessResponse(result, "Gastos obtenidos exitosamente"));
    }

    /// <summary>
    /// Obtener todos los gastos (sin paginación, para selectores)
    /// </summary>
    [HttpGet("all")]
    public async Task<ActionResult<ApiResponse<IEnumerable<ExpenseDto>>>> GetAllExpenses()
    {
        var query = new GetAllExpensesQuery();
        var result = await _mediator.Send(query);
        return Ok(ApiResponse<IEnumerable<ExpenseDto>>.SuccessResponse(result, "Gastos obtenidos exitosamente"));
    }

    /// <summary>
    /// Obtener gasto por ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> GetExpense(int id)
    {
        var query = new GetExpenseByIdQuery { Id = id };
        var result = await _mediator.Send(query);

        if (result == null)
            return NotFound(ApiResponse<ExpenseDto>.ErrorResponse("Gasto no encontrado"));

        return Ok(ApiResponse<ExpenseDto>.SuccessResponse(result, "Gasto obtenido exitosamente"));
    }

    /// <summary>
    /// Crear nuevo gasto (Admin y Superadmin)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Superadmin, Admin")]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> CreateExpense(
        [FromBody] CreateExpenseDto createDto)
    {
        var command = _mapper.Map<CreateExpenseCommand>(createDto);
        var result = await _mediator.Send(command);
        return CreatedAtAction(
            nameof(GetExpense),
            new { id = result.Id },
            ApiResponse<ExpenseDto>.SuccessResponse(result, "Gasto creado exitosamente"));
    }

    /// <summary>
    /// Actualizar gasto (Admin y Superadmin)
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Superadmin, Admin")]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> UpdateExpense(
        int id,
        [FromBody] UpdateExpenseDto updateDto)
    {
        var command = _mapper.Map<UpdateExpenseCommand>(updateDto);
        command.Id = id;

        var result = await _mediator.Send(command);
        return Ok(ApiResponse<ExpenseDto>.SuccessResponse(result, "Gasto actualizado exitosamente"));
    }

    /// <summary>
    /// Eliminar gasto (Admin y Superadmin)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Superadmin, Admin")]
    public async Task<ActionResult<ApiResponse>> DeleteExpense(int id)
    {
        var command = new DeleteExpenseCommand { Id = id };
        var result = await _mediator.Send(command);
        if (!result)
            return NotFound(ApiResponse.Error("Gasto no encontrado"));
        return Ok(ApiResponse.Success("Gasto eliminado exitosamente"));
    }
}


