namespace SenorArroz.Application.Common.Interfaces;

/// <summary>
/// Tokens FCM para push tipo pedido listo: domiciliarios activos de la sucursal
/// que no tienen pedidos activos asignados y cuya última ubicación reciente de
/// una jornada activa está dentro del radio configurado para la sucursal.
/// </summary>
public interface IFreeDeliverymanFcmTokenResolver
{
    Task<FreeDeliverymanFcmTokensResult> ResolveAsync(int branchId, CancellationToken cancellationToken = default);
}

public sealed record FreeDeliverymanFcmTokensResult(
    IReadOnlyList<string> Tokens,
    int BusyDeliverymanCount,
    int AtBranchDeliverymanCount);
