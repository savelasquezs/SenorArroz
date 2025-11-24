using MediatR;
using SenorArroz.Application.Features.Customers.DTOs;

namespace SenorArroz.Application.Features.Customers.Queries;

public class GetCustomerAddressByIdQuery : IRequest<CustomerAddressDto?>
{
    public int AddressId { get; set; }
}

