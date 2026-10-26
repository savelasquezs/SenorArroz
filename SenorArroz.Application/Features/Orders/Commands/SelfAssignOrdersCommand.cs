using MediatR;
using SenorArroz.Application.Features.Orders.DTOs;

namespace SenorArroz.Application.Features.Orders.Commands;

public class SelfAssignOrdersCommand : IRequest<List<OrderDto>>
{
    public bool ReplaceActiveRoute { get; set; }
    public List<int> OrderIds { get; set; } = new();
    public int? ProposalId { get; set; }
    public long? ExpectedPlanVersion { get; set; }
    public bool ModifiedProposal { get; set; }
    public string? SelectionFingerprint { get; set; }
}
