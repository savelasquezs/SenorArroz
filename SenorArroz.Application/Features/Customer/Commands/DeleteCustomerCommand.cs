using MediatR;

namespace SenorArroz.Application.Features.Customers.Commands;

public class DeleteCustomerCommand : IRequest<bool>
{
    public int Id { get; set; }
}