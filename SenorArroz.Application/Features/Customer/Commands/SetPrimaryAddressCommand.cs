using MediatR;
using SenorArroz.Application.Features.Customers.DTOs;

namespace SenorArroz.Application.Features.Customers.Commands;

public class SetPrimaryAddressCommand : IRequest<CustomerAddressDto>
{
    public int CustomerId { get; set; }
    public int AddressId { get; set; }
}
