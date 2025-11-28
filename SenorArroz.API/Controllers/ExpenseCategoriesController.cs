// SenorArroz.API/Controllers/ExpenseCategoriesController.cs
using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Features.ExpenseCategories.Commands;
using SenorArroz.Application.Features.ExpenseCategories.DTOs;
using SenorArroz.Application.Features.ExpenseCategories.Queries;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ExpenseCategoriesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IMapper _mapper;

    public ExpenseCategoriesController(IMediator mediator, IMapper mapper)
    {
        _mediator = mediator;
        _mapper = mapper;
    }

    /// <summary>
    /// Obtener categorías con paginación y filtros
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<ExpenseCategoryDto>>>> GetCategories(
        [FromQuery] ExpenseCategorySearchDto searchDto)
    {
        var query = _mapper.Map<GetExpenseCategoriesQuery>(searchDto);
        var result = await _mediator.Send(query);
        return Ok(ApiResponse<PagedResult<ExpenseCategoryDto>>.SuccessResponse(result, "Categorías obtenidas exitosamente"));
    }

    /// <summary>
    /// Obtener todas las categorías (sin paginación, para selectores)
    /// </summary>
    [HttpGet("all")]
    public async Task<ActionResult<ApiResponse<IEnumerable<ExpenseCategoryDto>>>> GetAllCategories()
    {
        var query = new GetAllExpenseCategoriesQuery();
        var result = await _mediator.Send(query);
        return Ok(ApiResponse<IEnumerable<ExpenseCategoryDto>>.SuccessResponse(result, "Categorías obtenidas exitosamente"));
    }

    /// <summary>
    /// Obtener categoría por ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ExpenseCategoryDto>>> GetCategory(int id)
    {
        var query = new GetExpenseCategoryByIdQuery { Id = id };
        var result = await _mediator.Send(query);

        if (result == null)
            return NotFound(ApiResponse<ExpenseCategoryDto>.ErrorResponse("Categoría no encontrada"));

        return Ok(ApiResponse<ExpenseCategoryDto>.SuccessResponse(result, "Categoría obtenida exitosamente"));
    }

    /// <summary>
    /// Crear nueva categoría (Admin y Superadmin)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Superadmin, Admin")]
    public async Task<ActionResult<ApiResponse<ExpenseCategoryDto>>> CreateCategory(
        [FromBody] CreateExpenseCategoryDto createDto)
    {
        var command = _mapper.Map<CreateExpenseCategoryCommand>(createDto);
        var result = await _mediator.Send(command);
        return CreatedAtAction(
            nameof(GetCategory),
            new { id = result.Id },
            ApiResponse<ExpenseCategoryDto>.SuccessResponse(result, "Categoría creada exitosamente"));
    }

    /// <summary>
    /// Actualizar categoría (Admin y Superadmin)
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Superadmin, Admin")]
    public async Task<ActionResult<ApiResponse<ExpenseCategoryDto>>> UpdateCategory(
        int id,
        [FromBody] UpdateExpenseCategoryDto updateDto)
    {
        var command = _mapper.Map<UpdateExpenseCategoryCommand>(updateDto);
        command.Id = id;

        var result = await _mediator.Send(command);
        return Ok(ApiResponse<ExpenseCategoryDto>.SuccessResponse(result, "Categoría actualizada exitosamente"));
    }

    /// <summary>
    /// Eliminar categoría (Admin y Superadmin)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Superadmin, Admin")]
    public async Task<ActionResult<ApiResponse>> DeleteCategory(int id)
    {
        var command = new DeleteExpenseCategoryCommand { Id = id };
        var result = await _mediator.Send(command);
        if (!result)
            return NotFound(ApiResponse.Error("Categoría no encontrada"));
        return Ok(ApiResponse.Success("Categoría eliminada exitosamente"));
    }
}

