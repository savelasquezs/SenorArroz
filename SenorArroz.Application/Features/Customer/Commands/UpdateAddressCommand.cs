using MediatR;
using SenorArroz.Application.Features.Customers.DTOs;

namespace SenorArroz.Application.Features.Customers.Commands;

public class UpdateAddressCommand : IRequest<CustomerAddressDto>
{
    public int Id { get; set; }
    public int NeighborhoodId { get; set; }
    public string Address { get; set; } = string.Empty;
    public string? AdditionalInfo { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public bool IsPrimary { get; set; } = false;
}