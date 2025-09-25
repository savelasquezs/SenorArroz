using MediatR;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Branches.Commands;

public class DeleteNeighborhoodHandler : IRequestHandler<DeleteNeighborhoodCommand, bool>
{
    private readonly INeighborhoodRepository _neighborhoodRepository;

    public DeleteNeighborhoodHandler(INeighborhoodRepository neighborhoodRepository)
    {
        _neighborhoodRepository = neighborhoodRepository;
    }

    public async Task<bool> Handle(DeleteNeighborhoodCommand request, CancellationToken cancellationToken)
    {
        return await _neighborhoodRepository.DeleteAsync(request.Id);
    }
}