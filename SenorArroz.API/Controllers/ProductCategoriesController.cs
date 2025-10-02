// SenorArroz.API/Controllers/ProductCategoriesController.cs
using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Features.Products.Commands;
using SenorArroz.Application.Features.Products.DTOs;
using SenorArroz.Application.Features.Products.Queries;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProductCategoriesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IMapper _mapper;

    public ProductCategoriesController(IMediator mediator, IMapper mapper)
    {
        _mediator = mediator;
        _mapper = mapper;
    }

    /// <summary>
    /// Obtener categorías con paginación y filtros
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<ProductCategoryDto>>>> GetCategories(
        [FromQuery] ProductCategorySearchDto searchDto)
    {
        var query = _mapper.Map<GetProductCategoriesQuery>(searchDto);
        var result = await _mediator.Send(query);
        return Ok(ApiResponse<PagedResult<ProductCategoryDto>>.SuccessResponse(result, "Categorías obtenidas exitosamente"));
    }

    /// <summary>
    /// Obtener categoría por ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ProductCategoryDto>>> GetCategory(int id)
    {
        var query = new GetProductCategoryByIdQuery { Id = id };
        var result = await _mediator.Send(query);

        if (result == null)
            return NotFound(ApiResponse<ProductCategoryDto>.ErrorResponse("Categoría no encontrada"));

        return Ok(ApiResponse<ProductCategoryDto>.SuccessResponse(result, "Categoría obtenida exitosamente"));
    }

    /// <summary>
    /// Crear nueva categoría (Admin y Superadmin)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Superadmin, Admin")]

    public async Task<ActionResult<ApiResponse<ProductCategoryDto>>> CreateCategory(
        [FromBody] CreateProductCategoryDto createDto)
    {
        var command = _mapper.Map<CreateProductCategoryCommand>(createDto);
        var result = await _mediator.Send(command);
        return CreatedAtAction(
            nameof(GetCategory),
            new { id = result.Id },
            ApiResponse<ProductCategoryDto>.SuccessResponse(result, "Categoría creada exitosamente"));
    }

    /// <summary>
    /// Actualizar categoría (Admin y Superadmin)
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Superadmin, Admin")]

    public async Task<ActionResult<ApiResponse<ProductCategoryDto>>> UpdateCategory(
        int id,
        [FromBody] UpdateProductCategoryDto updateDto)
    {
        var command = _mapper.Map<UpdateProductCategoryCommand>(updateDto);
        command.Id = id;

        var result = await _mediator.Send(command);
        return Ok(ApiResponse<ProductCategoryDto>.SuccessResponse(result, "Categoría actualizada exitosamente"));
    }

    /// <summary>
    /// Eliminar categoría (Admin y Superadmin)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Superadmin, Admin")]
    public async Task<ActionResult<ApiResponse>> DeleteCategory(int id)
    {
        var command = new DeleteProductCategoryCommand { Id = id };
        var result = await _mediator.Send(command);
        if (!result)
            return NotFound(ApiResponse.Error("Categoría no encontrada"));
        return Ok(ApiResponse.Success("Categoría eliminada exitosamente"));
    }
}