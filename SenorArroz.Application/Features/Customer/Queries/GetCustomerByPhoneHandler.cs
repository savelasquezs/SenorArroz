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
    public class GetCustomerByPhoneHandler : IRequestHandler<GetCustomerByPhoneQuery, CustomerDto?>
    {
        private readonly ICustomerRepository _customerRepository;
        private readonly IMapper _mapper;
        private readonly ICurrentUser _currentUser;
        private readonly ICurrentTenant _currentTenant;
        private readonly ILoyaltyCycleService _loyaltyCycle;

        public GetCustomerByPhoneHandler(
            ICustomerRepository customerRepository,
            IMapper mapper,
            ICurrentUser currentUser,
            ILoyaltyCycleService loyaltyCycle,
            ICurrentTenant currentTenant)
        {
            _customerRepository = customerRepository;
            _mapper = mapper;
            _currentUser = currentUser;
            _loyaltyCycle = loyaltyCycle;
            _currentTenant = currentTenant;
        }

        public async Task<CustomerDto?> Handle(GetCustomerByPhoneQuery request, CancellationToken cancellationToken)
        {
            var customer = await _customerRepository.GetByPhoneAsync(request.Phone, _currentTenant.TenantId, cancellationToken);
            if (customer == null)
                return null;

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
