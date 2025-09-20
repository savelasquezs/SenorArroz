using MediatR;
using SenorArroz.Application.Features.Customers.DTOs;

namespace SenorArroz.Application.Features.Customers.Queries;

public class GetCustomerAddressesQuery : IRequest<IEnumerable<CustomerAddressDto>>
{
    public int CustomerId { get; set; }
}