using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Helpers;

/// <summary>Shared safety and retry rules for the existing Rappi READY outbox.</summary>
public static class RappiReadyOutboxPolicy
{
    public const string EventType = "READY_FOR_PICKUP_OUTBOX";
    public const string Pending = "outbox_pending";
    public const string Processing = "outbox_processing";
    public const string ManualRetryPending = "outbox_retry_approved";
    public const string ReviewRequired = "outbox_review_required";
    public const string Failed = "outbox_failed";
    public const string Skipped = "outbox_skipped";
    public const int MaxAttempts = 3;
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(15);

    public static string EventKey(int connectionId, string orderId) =>
        $"READY_FOR_PICKUP:{connectionId}:{orderId}";

    public static bool CanSend(ExternalDeliveryOrder order) =>
        order.Connection.Provider == "rappi"
        && order.Connection.IsActive
        && order.Connection.IsVerified
        && order.Status == ExternalOrderStatus.Accepted
        && order.Store?.ManualReadyForPickupEnabled == true
        && order.InternalOrder?.Status == OrderStatus.Ready;

    public static bool CanRequeue(IntegrationWebhookEvent row) =>
        row.Status == Failed && row.AttemptCount < MaxAttempts;

    public static bool IsQueued(IntegrationWebhookEvent row) =>
        row.Status == Pending && row.AttemptCount == 0
        || row.Status == ManualRetryPending && row.AttemptCount < MaxAttempts;

    public static bool IsUncertain(int? statusCode) => statusCode is null or 408 or 424 or >= 500;

    // Provider messages and exception text can contain request data. Diagnostics
    // expose only a stable explanation and HTTP status, never a remote body.
    public static string FailureMessage(int? statusCode) =>
        statusCode.HasValue
            ? $"Rappi no confirmó READY_FOR_PICKUP (HTTP {statusCode.Value})."
            : "No fue posible confirmar READY_FOR_PICKUP con Rappi.";
}
