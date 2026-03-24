namespace SenorArroz.Application.Features.Orders.DTOs;

public class DeliverymanAssignedBranchSummaryDto
{
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public int OrderCount { get; set; }
}
