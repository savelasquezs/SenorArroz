namespace SenorArroz.Application.Common.Interfaces;

public interface IInventoryNotificationService
{
    Task NotifyChangedAsync(int branchId, string reason, CancellationToken cancellationToken);
    Task NotifyTransferPendingAsync(int destinationBranchId, int transferId, CancellationToken cancellationToken);
}
