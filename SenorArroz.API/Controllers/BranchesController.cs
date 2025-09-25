using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Features.Branches.Commands;
using SenorArroz.Application.Features.Branches.DTOs;
using SenorArroz.Application.Features.Branches.Queries;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BranchesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IMapper _mapper;

    public BranchesController(IMediator mediator, IMapper mapper)
    {
        _mediator = mediator;
        _mapper = mapper;
    }

    /// <summary>
    /// Obtener todas las sucursales (solo para superadmin)
    /// </summary>
    /// <returns>Lista de todas las sucursales</returns>
    [HttpGet("all")]
    [Authorize(Roles = "Superadmin")]
    public async Task<ActionResult<ApiResponse<PagedResult<BranchDto>>>> GetAllBranches()
    {
        var query = new GetBranchesQuery();
        var result = await _mediator.Send(query);
        return Ok(ApiResponse<PagedResult<BranchDto>>.SuccessResponse(result, "Sucursales obtenidas exitosamente"));
    }

    /// <summary>
    /// Obtener sucursales con paginación y filtros (solo para superadmin)
    /// </summary>
    /// <param name="searchDto">Parámetros de búsqueda</param>
    /// <returns>Lista paginada de sucursales</returns>
    [HttpGet]
    [Authorize(Roles = "Superadmin")]
    public async Task<ActionResult<ApiResponse<PagedResult<BranchDto>>>> GetBranches([FromQuery] BranchSearchDto searchDto)
    {
        var query = _mapper.Map<GetBranchesQuery>(searchDto);
        var result = await _mediator.Send(query);
        return Ok(ApiResponse<PagedResult<BranchDto>>.SuccessResponse(result, "Sucursales obtenidas exitosamente"));
    }

    /// <summary>
    /// Obtener sucursal por ID
    /// </summary>
    /// <param name="id">ID de la sucursal</param>
    /// <returns>Datos de la sucursal</returns>
    [HttpGet("{id}")]
    [Authorize(Roles = "Superadmin")]
    public async Task<ActionResult<ApiResponse<BranchDto>>> GetBranch(int id)
    {
        var query = new GetBranchByIdQuery { Id = id };
        var result = await _mediator.Send(query);

        if (result == null)
            return NotFound(ApiResponse<BranchDto>.ErrorResponse("Sucursal no encontrada"));

        return Ok(ApiResponse<BranchDto>.SuccessResponse(result, "Sucursal obtenida exitosamente"));
    }

    /// <summary>
    /// Obtener estadísticas de una sucursal
    /// </summary>
    /// <param name="id">ID de la sucursal</param>
    /// <returns>Estadísticas detalladas</returns>
    [HttpGet("{id}/stats")]
    [Authorize(Roles = "Superadmin")]
    public async Task<ActionResult<ApiResponse<BranchStatsDto>>> GetBranchStats(int id)
    {
        var query = new GetBranchStatsQuery { BranchId = id };
        var result = await _mediator.Send(query);
        return Ok(ApiResponse<BranchStatsDto>.SuccessResponse(result, "Estadísticas obtenidas exitosamente"));
    }

    /// <summary>
    /// Crear nueva sucursal
    /// </summary>
    /// <param name="createDto">Datos de la sucursal a crear</param>
    /// <returns>Sucursal creada</returns>
    [HttpPost]
    [Authorize(Roles = "Superadmin")]
    public async Task<ActionResult<ApiResponse<BranchDto>>> CreateBranch([FromBody] CreateBranchDto createDto)
    {
        var command = _mapper.Map<CreateBranchCommand>(createDto);
        var result = await _mediator.Send(command);
        return CreatedAtAction(
            nameof(GetBranch),
            new { id = result.Id },
            ApiResponse<BranchDto>.SuccessResponse(result, "Sucursal creada exitosamente"));
    }

    /// <summary>
    /// Actualizar sucursal
    /// </summary>
    /// <param name="id">ID de la sucursal</param>
    /// <param name="updateDto">Datos a actualizar</param>
    /// <returns>Sucursal actualizada</returns>
    [HttpPut("{id}")]
    [Authorize(Roles = "Superadmin")]
    public async Task<ActionResult<ApiResponse<BranchDto>>> UpdateBranch(int id, [FromBody] UpdateBranchDto updateDto)
    {
        var command = _mapper.Map<UpdateBranchCommand>(updateDto);
        command.Id = id;

        var result = await _mediator.Send(command);
        return Ok(ApiResponse<BranchDto>.SuccessResponse(result, "Sucursal actualizada exitosamente"));
    }

    /// <summary>
    /// Eliminar sucursal
    /// </summary>
    /// <param name="id">ID de la sucursal</param>
    /// <returns>Confirmación de eliminación</returns>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Superadmin")]
    public async Task<ActionResult<ApiResponse>> DeleteBranch(int id)
    {
        var command = new DeleteBranchCommand { Id = id };
        var result = await _mediator.Send(command);

        if (!result)
            return BadRequest(ApiResponse.Error("No se puede eliminar la sucursal. Puede tener usuarios, clientes o pedidos asociados."));

        return Ok(ApiResponse.Success("Sucursal eliminada exitosamente"));
    }

    /// <summary>
    /// Obtener barrios de una sucursal
    /// </summary>
    /// <param name="branchId">ID de la sucursal</param>
    /// <returns>Lista de barrios</returns>
    [HttpGet("{branchId}/neighborhoods")]
    public async Task<ActionResult<ApiResponse<IEnumerable<BranchNeighborhoodDto>>>> GetBranchNeighborhoods(int branchId)
    {
        var query = new GetBranchNeighborhoodsQuery { BranchId = branchId };
        var result = await _mediator.Send(query);
        return Ok(ApiResponse<IEnumerable<BranchNeighborhoodDto>>.SuccessResponse(result, "Barrios obtenidos exitosamente"));
    }

    /// <summary>
    /// Crear nuevo barrio en una sucursal
    /// </summary>
    /// <param name="branchId">ID de la sucursal</param>
    /// <param name="createNeighborhoodDto">Datos del barrio</param>
    /// <returns>Barrio creado</returns>
    [HttpPost("{branchId}/neighborhoods")]
    [Authorize(Roles = "Superadmin,Admin")]
    public async Task<ActionResult<ApiResponse<BranchNeighborhoodDto>>> CreateNeighborhood(
        int branchId,
        [FromBody] CreateNeighborhoodDto createNeighborhoodDto)
    {
        var command = _mapper.Map<CreateNeighborhoodCommand>(createNeighborhoodDto);
        command.BranchId = branchId;

        var result = await _mediator.Send(command);
        return CreatedAtAction(
            nameof(GetBranchNeighborhoods),
            new { branchId },
            ApiResponse<BranchNeighborhoodDto>.SuccessResponse(result, "Barrio creado exitosamente"));
    }

    /// <summary>
    /// Actualizar barrio
    /// </summary>
    /// <param name="branchId">ID de la sucursal</param>
    /// <param name="neighborhoodId">ID del barrio</param>
    /// <param name="updateNeighborhoodDto">Datos a actualizar</param>
    /// <returns>Barrio actualizado</returns>
    [HttpPut("{branchId}/neighborhoods/{neighborhoodId}")]
    [Authorize(Roles = "Superadmin,Admin")]
    public async Task<ActionResult<ApiResponse<BranchNeighborhoodDto>>> UpdateNeighborhood(
        int branchId,
        int neighborhoodId,
        [FromBody] UpdateNeighborhoodDto updateNeighborhoodDto)
    {
        var command = _mapper.Map<UpdateNeighborhoodCommand>(updateNeighborhoodDto);
        command.Id = neighborhoodId;

        var result = await _mediator.Send(command);
        return Ok(ApiResponse<BranchNeighborhoodDto>.SuccessResponse(result, "Barrio actualizado exitosamente"));
    }

    /// <summary>
    /// Eliminar barrio
    /// </summary>
    /// <param name="branchId">ID de la sucursal</param>
    /// <param name="neighborhoodId">ID del barrio</param>
    /// <returns>Confirmación de eliminación</returns>
    [HttpDelete("{branchId}/neighborhoods/{neighborhoodId}")]
    [Authorize(Roles = "Superadmin,Admin")]
    public async Task<ActionResult<ApiResponse>> DeleteNeighborhood(int branchId, int neighborhoodId)
    {
        var command = new DeleteNeighborhoodCommand { Id = neighborhoodId };
        var result = await _mediator.Send(command);

        if (!result)
            return BadRequest(ApiResponse.Error("No se puede eliminar el barrio. Puede tener direcciones o pedidos asociados."));

        return Ok(ApiResponse.Success("Barrio eliminado exitosamente"));
    }
}