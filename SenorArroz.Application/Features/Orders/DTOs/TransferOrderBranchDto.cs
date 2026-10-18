namespace SenorArroz.Application.Features.Orders.DTOs;

// Deliberately named TargetBranchId: it is not the current X-Branch-Id workspace.
public sealed class TransferOrderBranchDto
{
    public int TargetBranchId { get; set; }
}
