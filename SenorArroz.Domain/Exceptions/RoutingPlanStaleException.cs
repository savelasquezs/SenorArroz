namespace SenorArroz.Domain.Exceptions;

public sealed class RoutingPlanStaleException : Exception
{
    public const string ErrorCode = "ROUTING_PLAN_STALE";

    public RoutingPlanStaleException(string message = "La ruta cambio porque otro domiciliario tomo pedidos.")
        : base(message)
    {
    }
}
