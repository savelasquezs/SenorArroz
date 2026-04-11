namespace SenorArroz.Application.Common.Interfaces;

/// <summary>
/// Tokens FCM de domiciliarios activos y "libres" (sin pedido OnTheWay) en una sucursal.
/// Misma regla que el push de pedido listo.
/// </summary>
public interface IFreeDeliverymanFcmTokenResolver
{
    Task<FreeDeliverymanFcmTokensResult> ResolveAsync(int branchId, CancellationToken cancellationToken = default);
}

public sealed record FreeDeliverymanFcmTokensResult(
    IReadOnlyList<string> Tokens,
    int BusyDeliverymanCount);
