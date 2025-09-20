using MediatR;

namespace SenorArroz.Application.Features.Customers.Commands;

public class DeleteAddressCommand : IRequest<bool>
{
    public int Id { get; set; }
}