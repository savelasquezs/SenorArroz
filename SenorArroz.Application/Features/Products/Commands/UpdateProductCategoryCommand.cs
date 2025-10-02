// SenorArroz.Application/Features/Products/Commands/UpdateProductCategoryCommand.cs
using MediatR;
using SenorArroz.Application.Features.Products.DTOs;

namespace SenorArroz.Application.Features.Products.Commands;

public class UpdateProductCategoryCommand : IRequest<ProductCategoryDto>
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}