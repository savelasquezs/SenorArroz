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
        // First, verify the customer exists and user has access to it
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId);
        if (customer == null)
        {
            throw new BusinessException("Cliente no encontrado");
        }

        // Check if user has access to this customer's branch
        if (_currentUser.Role != "superadmin" && customer.BranchId != _currentUser.BranchId)
        {
            throw new BusinessException("No tienes permisos para acceder a las direcciones de este cliente");
        }

        var addresses = await _addressRepository.GetByCustomerIdAsync(request.CustomerId);
        return _mapper.Map<IEnumerable<CustomerAddressDto>>(addresses);
    }
}
