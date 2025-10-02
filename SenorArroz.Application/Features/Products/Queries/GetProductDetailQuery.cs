// SenorArroz.Application/Features/Products/Queries/GetProductDetailQuery.cs
using MediatR;
using SenorArroz.Application.Features.Products.DTOs;

namespace SenorArroz.Application.Features.Products.Queries;

public class GetProductDetailQuery : IRequest<ProductDetailDto?>
{
    public int Id { get; set; }
}
