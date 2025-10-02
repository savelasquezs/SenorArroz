// SenorArroz.Application/Features/Products/Queries/GetProductCategoryByIdQuery.cs
using MediatR;
using SenorArroz.Application.Features.Products.DTOs;

namespace SenorArroz.Application.Features.Products.Queries;

public class GetProductCategoryByIdQuery : IRequest<ProductCategoryDto?>
{
    public int Id { get; set; }
}