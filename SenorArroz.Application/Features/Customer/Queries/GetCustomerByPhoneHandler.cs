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

        public GetCustomerByPhoneHandler(ICustomerRepository customerRepository, IMapper mapper, ICurrentUser currentUser)
        {
            _customerRepository = customerRepository;
            _mapper = mapper;
            _currentUser = currentUser;
        }

        public async Task<CustomerDto?> Handle(GetCustomerByPhoneQuery request, CancellationToken cancellationToken)
        {
            // Determine branch filter based on user role
            int branchFilter = _currentUser.Role == "superadmin" ? request.BranchId : _currentUser.BranchId;

            var customer = await _customerRepository.GetByPhoneAsync(request.Phone, branchFilter);
            if (customer == null)
                return null;

            // Additional check for non-superadmin users
            if (_currentUser.Role != "superadmin" && customer.BranchId != _currentUser.BranchId)
            {
                throw new BusinessException("No tienes permisos para acceder a este cliente");
            }

            var customerDto = _mapper.Map<CustomerDto>(customer);

            // Add additional data
            customerDto.TotalOrders = await _customerRepository.GetTotalOrdersAsync(customer.Id);
            customerDto.LastOrderDate = await _customerRepository.GetLastOrderDateAsync(customer.Id);

            return customerDto;
        }
    }
}
