using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Interfaces;

public interface IPrintQueueService
{
    Task<bool> IsAgentTokenValidAsync(int branchId, string? plainToken, CancellationToken cancellationToken = default);

    Task<PrintJob> EnqueueAsync(int branchId, PrintJobKind kind, IReadOnlyList<int> orderIds, CancellationToken cancellationToken = default);

    Task<PrintJob> EnqueueDeliveryAsync(
        int branchId,
        IReadOnlyList<int> orderIds,
        int? deliverymanUserId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Encola un trabajo con payload sintético (cocina o domicilio) para probar el agente sin pedido real.</summary>
    Task<PrintJob> EnqueueTestPrintAsync(int branchId, PrintJobKind kind, CancellationToken cancellationToken = default);

    /// <summary>Valida que los pedidos sean domicilio, en ruta y asignados al domiciliario (reimpresión / POST desde app móvil).</summary>
    Task ValidateDeliverymanDeliveryEnqueueAsync(
        int branchId,
        int deliverymanUserId,
        IReadOnlyList<int> orderIds,
        CancellationToken cancellationToken = default);

    /// <summary>Marca jobs pendientes como <see cref="PrintJobStatus.Processing"/> y los devuelve (SKIP LOCKED).</summary>
    Task<IReadOnlyList<PrintJobAgentItemDto>> ClaimPendingForAgentAsync(
        int branchId,
        IReadOnlyList<PrintJobKind> kinds,
        int take,
        CancellationToken cancellationToken = default);

    Task<PrintJobAgentItemDto?> ClaimSpecificForAgentAsync(
        int branchId,
        long jobId,
        CancellationToken cancellationToken = default);

    Task<PrintJobStatusDto?> GetJobStatusAsync(
        int branchId,
        long jobId,
        int? deliverymanUserId = null,
        CancellationToken cancellationToken = default);

    Task<bool> TryCompleteJobAsync(int branchId, long jobId, CancellationToken cancellationToken = default);

    Task<bool> TryFailJobAsync(int branchId, long jobId, string message, CancellationToken cancellationToken = default);
}

public interface IPrintAgentNotifier
{
    Task NotifyJobsAvailableAsync(
        int branchId,
        long jobId,
        PrintJobKind kind,
        CancellationToken cancellationToken = default);
}

public record PrintJobAgentItemDto(
    long Id,
    string Kind,
    string OrderIdsJson,
    string PayloadJson,
    short PayloadVersion);

public record PrintJobStatusDto(
    long JobId,
    string Status,
    string Kind,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? ErrorMessage);
