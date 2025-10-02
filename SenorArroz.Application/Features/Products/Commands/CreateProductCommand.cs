// SenorArroz.Application/Features/Products/Commands/CreateProductCommand.cs
using MediatR;
using SenorArroz.Application.Features.Products.DTOs;

namespace SenorArroz.Application.Features.Products.Commands;

public class CreateProductCommand : IRequest<ProductDto>
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Price { get; set; }
    public int? Stock { get; set; }
    public bool Active { get; set; } = true;
}
