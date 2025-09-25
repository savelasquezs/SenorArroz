using MediatR;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Branches.Commands;

public class DeleteBranchHandler : IRequestHandler<DeleteBranchCommand, bool>
{
    private readonly IBranchRepository _branchRepository;

    public DeleteBranchHandler(IBranchRepository branchRepository)
    {
        _branchRepository = branchRepository;
    }

    public async Task<bool> Handle(DeleteBranchCommand request, CancellationToken cancellationToken)
    {
        return await _branchRepository.DeleteAsync(request.Id);
    }
}