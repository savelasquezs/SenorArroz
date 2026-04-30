namespace SenorArroz.Application.Common.Interfaces;

/// <summary>
/// Tokens FCM para push tipo pedido listo: domiciliarios activos de la sucursal que hoy (calendario Colombia)
/// tienen al menos una asignación registrada, no están bloqueados por liquidación total del día,
/// y no tienen pedido en OnTheWay (ocupados en ruta).
/// </summary>
public interface IFreeDeliverymanFcmTokenResolver
{
    Task<FreeDeliverymanFcmTokensResult> ResolveAsync(int branchId, CancellationToken cancellationToken = default);
}

public sealed record FreeDeliverymanFcmTokensResult(
    IReadOnlyList<string> Tokens,
    int BusyDeliverymanCount);
