using MediatR;
using SenorArroz.Application.Features.Customers.DTOs;

namespace SenorArroz.Application.Features.Customers.Commands
{
    public class UpdateCustomerCommand : IRequest<CustomerDto>
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Phone1 { get; set; } = string.Empty;
        public string? Phone2 { get; set; }
        public bool Active { get; set; }
    }
}