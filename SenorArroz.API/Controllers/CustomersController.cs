using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SenorArroz.Application.Features.Customers.Commands;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Application.Features.Customers.Queries;
using SenorArroz.Shared.Models;
using System.Security.Claims;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CustomersController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IMapper _mapper;

    public CustomersController(IMediator mediator, IMapper mapper)
    {
        _mediator = mediator;
        _mapper = mapper;
    }

    /// <summary>
    /// Obtener clientes con paginación y filtros
    /// </summary>
    /// <param name="searchDto">Parámetros de búsqueda</param>
    /// <returns>Lista paginada de clientes</returns>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<CustomerDto>>>> GetCustomers([FromQuery] CustomerSearchDto searchDto)
    {
        var branchId = GetCurrentUserBranchId();
        if (branchId == null)
            return Unauthorized();

        var query = _mapper.Map<GetCustomersQuery>(searchDto);
        query.BranchId = branchId.Value;

        var result = await _mediator.Send(query);
        return Ok(ApiResponse<PagedResult<CustomerDto>>.SuccessResponse(result, "Clientes obtenidos exitosamente"));
    }

    /// <summary>
    /// Obtener cliente por ID
    /// </summary>
    /// <param name="id">ID del cliente</param>
    /// <returns>Datos del cliente</returns>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> GetCustomer(int id)
    {
        var query = new GetCustomerByIdQuery { Id = id };
        var result = await _mediator.Send(query);

        if (result == null)
            return NotFound(ApiResponse<CustomerDto>.ErrorResponse("Cliente no encontrado"));

        return Ok(ApiResponse<CustomerDto>.SuccessResponse(result, "Cliente obtenido exitosamente"));
    }

    /// <summary>
    /// Buscar cliente por teléfono
    /// </summary>
    /// <param name="phone">Número de teléfono</param>
    /// <returns>Datos del cliente</returns>
    [HttpGet("by-phone/{phone}")]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> GetCustomerByPhone(string phone)
    {
        var branchId = GetCurrentUserBranchId();
        if (branchId == null)
            return Unauthorized();

        var query = new GetCustomerByPhoneQuery
        {
            Phone = phone,
            BranchId = branchId.Value
        };

        var result = await _mediator.Send(query);

        if (result == null)
            return NotFound(ApiResponse<CustomerDto>.ErrorResponse("Cliente no encontrado"));

        return Ok(ApiResponse<CustomerDto>.SuccessResponse(result, "Cliente obtenido exitosamente"));
    }

    /// <summary>
    /// Crear nuevo cliente
    /// </summary>
    /// <param name="createDto">Datos del cliente a crear</param>
    /// <returns>Cliente creado</returns>
    [HttpPost]
    [Authorize(Roles ="Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> CreateCustomer([FromBody] CreateCustomerDto createDto)
    {
        var branchId = GetCurrentUserBranchId();
        string? currentUserRole = GetCurrentUserRole();
        
        if (branchId == null)
            return Unauthorized();

        var command = _mapper.Map<CreateCustomerCommand>(createDto);
        if(currentUserRole!="superadmin")
            command.BranchId = branchId.Value;
        
        var result = await _mediator.Send(command);
        return CreatedAtAction(
            nameof(GetCustomer),
            new { id = result.Id },
            ApiResponse<CustomerDto>.SuccessResponse(result, "Cliente creado exitosamente"));
    }

    /// <summary>
    /// Actualizar cliente
    /// </summary>
    /// <param name="id">ID del cliente</param>
    /// <param name="updateDto">Datos a actualizar</param>
    /// <returns>Cliente actualizado</returns>
    [HttpPut("{id}")]
    [Authorize(Roles ="Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<CustomerDto>>> UpdateCustomer(int id, [FromBody] UpdateCustomerDto updateDto)
    {
        var command = _mapper.Map<UpdateCustomerCommand>(updateDto);
        command.Id = id;

        var result = await _mediator.Send(command);
        return Ok(ApiResponse<CustomerDto>.SuccessResponse(result, "Cliente actualizado exitosamente"));
    }

    /// <summary>
    /// Eliminar cliente (soft delete)
    /// </summary>
    /// <param name="id">ID del cliente</param>
    /// <returns>Confirmación de eliminación</returns>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Superadmin,Admin")]
    public async Task<ActionResult<ApiResponse>> DeleteCustomer(int id)
    {
        var command = new DeleteCustomerCommand { Id = id };
        var result = await _mediator.Send(command);

        if (!result)
            return NotFound(ApiResponse.Error("Cliente no encontrado"));

        return Ok(ApiResponse.Success("Cliente eliminado exitosamente"));
    }

    /// <summary>
    /// Obtener direcciones de un cliente
    /// </summary>
    /// <param name="customerId">ID del cliente</param>
    /// <returns>Lista de direcciones</returns>
    [HttpGet("{customerId}/addresses")]
    public async Task<ActionResult<ApiResponse<IEnumerable<CustomerAddressDto>>>> GetCustomerAddresses(int customerId)
    {
        var query = new GetCustomerAddressesQuery { CustomerId = customerId };
        var result = await _mediator.Send(query);

        return Ok(ApiResponse<IEnumerable<CustomerAddressDto>>.SuccessResponse(result, "Direcciones obtenidas exitosamente"));
    }

    /// <summary>
    /// Crear nueva dirección para un cliente
    /// </summary>
    /// <param name="customerId">ID del cliente</param>
    /// <param name="createAddressDto">Datos de la dirección</param>
    /// <returns>Dirección creada</returns>
    [HttpPost("{customerId}/addresses")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<CustomerAddressDto>>> CreateCustomerAddress(
        int customerId,
        [FromBody] CreateCustomerAddressDto createAddressDto)
    {
        var command = _mapper.Map<CreateAddressCommand>(createAddressDto);
        command.CustomerId = customerId;

        var result = await _mediator.Send(command);
        return CreatedAtAction(
            nameof(GetCustomerAddresses),
            new { customerId },
            ApiResponse<CustomerAddressDto>.SuccessResponse(result, "Dirección creada exitosamente"));
    }

    /// <summary>
    /// Actualizar dirección de cliente
    /// </summary>
    /// <param name="customerId">ID del cliente</param>
    /// <param name="addressId">ID de la dirección</param>
    /// <param name="updateAddressDto">Datos a actualizar</param>
    /// <returns>Dirección actualizada</returns>
    [HttpPut("{customerId}/addresses/{addressId}")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<CustomerAddressDto>>> UpdateCustomerAddress(
        int customerId,
        int addressId,
        [FromBody] UpdateCustomerAddressDto updateAddressDto)
    {
        var command = _mapper.Map<UpdateAddressCommand>(updateAddressDto);
        command.Id = addressId;

        var result = await _mediator.Send(command);
        return Ok(ApiResponse<CustomerAddressDto>.SuccessResponse(result, "Dirección actualizada exitosamente"));
    }

    /// <summary>
    /// Eliminar dirección de cliente
    /// </summary>
    /// <param name="customerId">ID del cliente</param>
    /// <param name="addressId">ID de la dirección</param>
    /// <returns>Confirmación de eliminación</returns>
    [HttpDelete("{customerId}/addresses/{addressId}")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse>> DeleteCustomerAddress(int customerId, int addressId)
    {
        var command = new DeleteAddressCommand { Id = addressId };
        var result = await _mediator.Send(command);

        if (!result)
            return NotFound(ApiResponse.Error("Dirección no encontrada o no se puede eliminar"));

        return Ok(ApiResponse.Success("Dirección eliminada exitosamente"));
    }

    /// <summary>
    /// Establecer dirección como primaria
    /// </summary>
    /// <param name="customerId">ID del cliente</param>
    /// <param name="addressId">ID de la dirección</param>
    /// <returns>Dirección actualizada como primaria</returns>
    [HttpPut("{customerId}/addresses/{addressId}/set-primary")]
    [Authorize(Roles = "Superadmin, Admin, Cashier")]
    public async Task<ActionResult<ApiResponse<CustomerAddressDto>>> SetPrimaryAddress(int customerId, int addressId)
    {
        var command = new SetPrimaryAddressCommand 
        { 
            CustomerId = customerId, 
            AddressId = addressId 
        };

        var result = await _mediator.Send(command);
        return Ok(ApiResponse<CustomerAddressDto>.SuccessResponse(result, "Dirección establecida como primaria exitosamente"));
    }

    /// <summary>
    /// Obtener barrios disponibles para una sucursal
    /// </summary>
    /// <returns>Lista de barrios</returns>
    [HttpGet("neighborhoods")]
    public async Task<ActionResult<ApiResponse<IEnumerable<NeighborhoodDto>>>> GetNeighborhoods()
    {
        var branchId = GetCurrentUserBranchId();
        if (branchId == null)
            return Unauthorized();

        var query = new GetNeighborhoodsQuery { BranchId = branchId.Value };
        var result = await _mediator.Send(query);

        var neighborhoods = result.Select(n => new NeighborhoodDto
        {
            Id = n.Id,
            Name = n.Name,
            DeliveryFee = n.DeliveryFee
        });

        return Ok(ApiResponse<IEnumerable<NeighborhoodDto>>
                  .SuccessResponse(neighborhoods, "Barrios obtenidos exitosamente"));
    }

    #region Private Methods

    private int? GetCurrentUserBranchId()
    {
        var branchIdClaim = User.FindFirst("branch_id");
        return branchIdClaim != null && int.TryParse(branchIdClaim.Value, out var branchId) ? branchId : null;
    }

    private string? GetCurrentUserRole()
    {
        return User.FindFirst(ClaimTypes.Role)?.Value;
    }

    #endregion
}