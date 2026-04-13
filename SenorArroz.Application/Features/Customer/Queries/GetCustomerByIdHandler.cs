using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SenorArroz.Application.Features.Customers.Queries
{
    public class GetCustomerByIdHandler : IRequestHandler<GetCustomerByIdQuery, CustomerDto?>
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly IMapper _mapper;
        private readonly ICurrentUser _currentUser;
        private readonly ILoyaltyCycleService _loyaltyCycle;

        public GetCustomerByIdHandler(
            ICustomerRepository customerRepository,
            IMapper mapper,
            ICurrentUser currentUser,
            ILoyaltyCycleService loyaltyCycle)
        {
            _customerRepository = customerRepository;
            _mapper = mapper;
            _currentUser = currentUser;
            _loyaltyCycle = loyaltyCycle;
        }

        public async Task<CustomerDto?> Handle(GetCustomerByIdQuery request, CancellationToken cancellationToken)
        {
            var customer = await _customerRepository.GetByIdWithAddressesAsync(request.Id, cancellationToken);
            if (customer == null)
                return null;

            // Check if user has access to this customer's branch
            if (!Roles.IsSuperadmin(_currentUser.Role) && customer.BranchId != _currentUser.BranchId)
            {
                throw new BusinessException("No tienes permisos para acceder a este cliente");
            }

            var customerDto = _mapper.Map<CustomerDto>(customer);

            // Add additional data
            customerDto.TotalOrders = await _customerRepository.GetTotalOrdersAsync(customer.Id, cancellationToken);
            var (first, last) = await _customerRepository.GetOrderDateRangeAsync(customer.Id, cancellationToken);
            customerDto.FirstOrderDate = first;
            customerDto.LastOrderDate = last;
            customerDto.TotalAccumulated = await _customerRepository.GetTotalOrderRevenueAsync(customer.Id, cancellationToken);
            await _loyaltyCycle.ApplyLoyaltyPreviewToCustomerDtoAsync(customerDto, cancellationToken);

            return customerDto;
        }
    }

}
