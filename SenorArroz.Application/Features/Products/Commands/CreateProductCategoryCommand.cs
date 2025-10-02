// SenorArroz.Application/Features/Products/Commands/CreateProductCategoryCommand.cs
using MediatR;
using SenorArroz.Application.Features.Products.DTOs;

namespace SenorArroz.Application.Features.Products.Commands;

public class CreateProductCategoryCommand : IRequest<ProductCategoryDto>
{
    public string Name { get; set; } = string.Empty;
    public int? BranchId { get; set; } // Optional, for superadmin
}