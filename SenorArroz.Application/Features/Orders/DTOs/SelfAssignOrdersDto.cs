namespace SenorArroz.Application.Features.Orders.DTOs;

public class SelfAssignOrdersDto
{
    public List<int> OrderIds { get; set; } = new();
    public int? ProposalId { get; set; }
    public long? ExpectedPlanVersion { get; set; }
    public bool ModifiedProposal { get; set; }
    public string? SelectionFingerprint { get; set; }
}

