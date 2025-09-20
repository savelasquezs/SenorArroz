using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Customers.Queries;

public class GetCustomerAddressesHandler : IRequestHandler<GetCustomerAddressesQuery, IEnumerable<CustomerAddressDto>>
{
    private readonly IAddressRepository _addressRepository;
    private readonly IMapper _mapper;

    public GetCustomerAddressesHandler(IAddressRepository addressRepository, IMapper mapper)
    {
        _addressRepository = addressRepository;
        _mapper = mapper;
    }

    public async Task<IEnumerable<CustomerAddressDto>> Handle(GetCustomerAddressesQuery request, CancellationToken cancellationToken)
    {
        var addresses = await _addressRepository.GetByCustomerIdAsync(request.CustomerId);
        return _mapper.Map<IEnumerable<CustomerAddressDto>>(addresses);
    }
}
