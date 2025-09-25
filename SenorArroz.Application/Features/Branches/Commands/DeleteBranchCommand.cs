using MediatR;

namespace SenorArroz.Application.Features.Branches.Commands;

public class DeleteBranchCommand : IRequest<bool>
{
    public int Id { get; set; }
}