namespace SenorArroz.Domain.Exceptions;

public sealed class OrderConcurrencyException() : Exception(
    "El pedido cambió desde que lo abriste. Actualiza los datos y revisa los cambios antes de guardar otra vez.")
{
    public const string ErrorCode = "ORDER_VERSION_CONFLICT";
}
