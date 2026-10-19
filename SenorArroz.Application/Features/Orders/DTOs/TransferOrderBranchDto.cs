namespace SenorArroz.Application.Features.Orders.DTOs;

// TargetBranchId is the destination, not the X-Branch-Id workspace.
public sealed class TransferOrderBranchDto
{
    public int TargetBranchId { get; set; }
    // Omitted or empty: keep every existing bank payment in its current bank.
    public List<TransferOrderBankPaymentDto> BankPaymentChanges { get; set; } = [];
}

public sealed record TransferOrderBankPaymentDto(int PaymentId, int TargetBankId);
