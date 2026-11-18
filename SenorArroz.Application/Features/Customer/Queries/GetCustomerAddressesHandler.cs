using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Customers.Queries;

public class GetCustomerAddressesHandler : IRequestHandler<GetCustomerAddressesQuery, IEnumerable<CustomerAddressDto>>
{
    private readonly IAddressRepository _addressRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetCustomerAddressesHandler(IAddressRepository addressRepository, ICustomerRepository customerRepository, IMapper mapper, ICurrentUser currentUser)
    {
        _addressRepository = addressRepository;
        _customerRepository = customerRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<IEnumerable<CustomerAddressDto>> Handle(GetCustomerAddressesQuery request, CancellationToken cancellationToken)
    {
        // Verify tenant-scoped existence without loading unrelated customer phones/branch.
        var customerExists = await _customerRepository.ExistsAsync(request.CustomerId, cancellationToken);
        if (!customerExists)
        {
            throw new BusinessException("Cliente no encontrado");
        }

        var addresses = await _addressRepository.GetByCustomerIdAsync(request.CustomerId, cancellationToken);
        return _mapper.Map<IEnumerable<CustomerAddressDto>>(addresses);
    }
}
