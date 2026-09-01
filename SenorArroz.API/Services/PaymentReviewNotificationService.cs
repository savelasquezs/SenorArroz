using Microsoft.AspNetCore.SignalR;
using SenorArroz.API.Hubs;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.API.Services;

public sealed class PaymentReviewNotificationService(IHubContext<OrderHub> hubContext) : IPaymentReviewNotificationService
{
    public Task NotifyReviewRequiredAsync(
        int branchId,
        int orderId,
        int paymentAttemptId,
        string reason,
        CancellationToken cancellationToken) =>
        hubContext.Clients.Group($"Branch_{branchId}_Admin").SendAsync(
            "PaymentReviewRequired",
            new { branchId, orderId, paymentAttemptId, reason },
            cancellationToken);
}
