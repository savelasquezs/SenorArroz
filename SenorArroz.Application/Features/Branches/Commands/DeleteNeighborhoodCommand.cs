using MediatR;

namespace SenorArroz.Application.Features.Branches.Commands;

public class DeleteNeighborhoodCommand : IRequest<bool>
{
    public int Id { get; set; }
}
