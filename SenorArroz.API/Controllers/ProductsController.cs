// SenorArroz.API/Controllers/ProductsController.cs
using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Products.Commands;
using SenorArroz.Application.Features.Products.DTOs;
using SenorArroz.Application.Features.Products.Queries;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProductsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IMapper _mapper;
    private readonly IApplicationDbContext _db;
    private readonly IBranchContext _branchContext;

    public ProductsController(IMediator mediator, IMapper mapper, IApplicationDbContext db, IBranchContext branchContext)
    {
        _mediator = mediator;
        _mapper = mapper;
        _db = db;
        _branchContext = branchContext;
    }

    /// <summary>
    /// Obtener productos con paginación y filtros
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<ProductDto>>>> GetProducts(
        [FromQuery] ProductSearchDto searchDto)
    {
        var query = _mapper.Map<GetProductsQuery>(searchDto);
        var result = await _mediator.Send(query);
        return Ok(ApiResponse<PagedResult<ProductDto>>.SuccessResponse(result, "Productos obtenidos exitosamente"));
    }

    /// <summary>
    /// Obtener producto por ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ProductDto>>> GetProduct(int id)
    {
        var query = new GetProductByIdQuery { Id = id };
        var result = await _mediator.Send(query);

        if (result == null)
            return NotFound(ApiResponse<ProductDto>.ErrorResponse("Producto no encontrado"));

        return Ok(ApiResponse<ProductDto>.SuccessResponse(result, "Producto obtenido exitosamente"));
    }

    /// <summary>
    /// Obtener detalles completos del producto con estadísticas
    /// </summary>
    [HttpGet("{id}/detail")]
    public async Task<ActionResult<ApiResponse<ProductDetailDto>>> GetProductDetail(int id)
    {
        var query = new GetProductDetailQuery { Id = id };
        var result = await _mediator.Send(query);

        if (result == null)
            return NotFound(ApiResponse<ProductDetailDto>.ErrorResponse("Producto no encontrado"));

        return Ok(ApiResponse<ProductDetailDto>.SuccessResponse(result, "Detalles del producto obtenidos exitosamente"));
    }

    /// <summary>
    /// Crear nuevo producto (Admin y Superadmin)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Superadmin, Admin")]
    public async Task<ActionResult<ApiResponse<ProductDto>>> CreateProduct(
        [FromBody] CreateProductDto createDto)
    {
        var command = _mapper.Map<CreateProductCommand>(createDto);
        var result = await _mediator.Send(command);
        return CreatedAtAction(
            nameof(GetProduct),
            new { id = result.Id },
            ApiResponse<ProductDto>.SuccessResponse(result, "Producto creado exitosamente"));
    }

    /// <summary>
    /// Actualizar producto (Admin y Superadmin)
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Superadmin, Admin")]
    public async Task<ActionResult<ApiResponse<ProductDto>>> UpdateProduct(
        int id,
        [FromBody] UpdateProductDto updateDto)
    {
        var command = _mapper.Map<UpdateProductCommand>(updateDto);
        command.Id = id;

        var result = await _mediator.Send(command);
        return Ok(ApiResponse<ProductDto>.SuccessResponse(result, "Producto actualizado exitosamente"));
    }

    /// <summary>
    /// Eliminar producto (Admin y Superadmin)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Superadmin, Admin")]
    public async Task<ActionResult<ApiResponse>> DeleteProduct(int id)
    {
        var command = new DeleteProductCommand { Id = id };
        var result = await _mediator.Send(command);
        if (!result)
            return NotFound(ApiResponse.Error("Producto no encontrado"));
        return Ok(ApiResponse.Success("Producto eliminado exitosamente"));
    }

    /// <summary>
    /// Ajustar stock del producto (Admin y Superadmin)
    /// </summary>
    [HttpPut("{id}/stock")]
    [Authorize(Roles = "Superadmin, Admin")]
    public async Task<ActionResult<ApiResponse<int>>> AdjustStock(int id, [FromBody] AdjustStockDto adjustDto)
    {
        var product = await _db.Products.Include(x => x.Category).SingleOrDefaultAsync(x => x.Id == id);
        if (product is null) return NotFound(ApiResponse<int>.ErrorResponse("Producto no encontrado"));
        _branchContext.EnsureAccess(product.Category.BranchId);
        if (product.InventoryEnabled)
            return Conflict(ApiResponse<int>.ErrorResponse("El producto usa el inventario nuevo; ajusta sus insumos desde Inventario"));
        product.Stock = Math.Max(0, (product.Stock ?? 0) + adjustDto.Quantity);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<int>.SuccessResponse(product.Stock.Value, "Stock ajustado exitosamente"));
    }
}

// DTO for stock adjustment
public class AdjustStockDto
{
    public int Quantity { get; set; }
}
