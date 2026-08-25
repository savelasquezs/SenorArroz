// SenorArroz.Application/Features/Products/Commands/UpdateProductCommand.cs
using MediatR;
using SenorArroz.Application.Features.Products.DTOs;

namespace SenorArroz.Application.Features.Products.Commands;

public class UpdateProductCommand : IRequest<ProductDto>
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Price { get; set; }
    public int? Stock { get; set; }
    public int? WeightGrams { get; set; }
    public bool Active { get; set; }
    public int? CommercialProfileId { get; set; }
    public int? ServesPeopleMin { get; set; }
    public int? ServesPeopleMax { get; set; }
    public string? StorefrontVariantLabel { get; set; }
    public int StorefrontSortOrder { get; set; }
}
