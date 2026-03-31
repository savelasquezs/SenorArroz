namespace SenorArroz.Application.Common.Services;

/// <summary>
/// Fidelidad por ciclo: el premio corresponde cada <see cref="DefaultInterval"/> pedidos entregados
/// (5.º, 10.º, 15.º, …). El <see cref="LoyaltyCycleStep.StepIndex"/> avanza en cada hito.
/// </summary>
public static class LoyaltyDeliveriesPerReward
{
    public const int DefaultInterval = 5;

    /// <summary>Próximo total de entregas en el que corresponde premio (p. ej. 5, 10 si ya hay 5).</summary>
    public static int GetNextRewardMilestoneDeliveries(int deliveredCount, int interval = DefaultInterval)
    {
        if (interval <= 0)
            throw new ArgumentOutOfRangeException(nameof(interval));
        if (deliveredCount < 0)
            throw new ArgumentOutOfRangeException(nameof(deliveredCount));

        if (deliveredCount == 0)
            return interval;

        var rem = deliveredCount % interval;
        return rem == 0 ? deliveredCount + interval : deliveredCount + (interval - rem);
    }

    /// <summary>Entregas que faltan hasta el próximo premio.</summary>
    public static int GetDeliveriesUntilNextReward(int deliveredCount, int interval = DefaultInterval)
    {
        if (interval <= 0)
            throw new ArgumentOutOfRangeException(nameof(interval));
        if (deliveredCount < 0)
            throw new ArgumentOutOfRangeException(nameof(deliveredCount));

        var rem = deliveredCount % interval;
        return rem == 0 ? interval : interval - rem;
    }

    /// <summary>StepIndex (1..cycleLength) del premio al alcanzar <paramref name="milestoneDeliveries"/> entregas.</summary>
    public static int GetStepIndexAtMilestone(int milestoneDeliveries, int cycleLength, int interval = DefaultInterval)
    {
        if (milestoneDeliveries <= 0 || milestoneDeliveries % interval != 0)
            throw new ArgumentException("El hito debe ser un múltiplo positivo del intervalo.", nameof(milestoneDeliveries));
        if (cycleLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(cycleLength));

        var block = milestoneDeliveries / interval;
        return ((block - 1) % cycleLength) + 1;
    }

    /// <summary>Tras marcar entregado, <paramref name="deliveredCountIncludingCurrent"/> incluye ese pedido.</summary>
    public static bool TryGetStepIndexForDeliveredCount(
        int deliveredCountIncludingCurrent,
        int cycleLength,
        int interval,
        out int stepIndex)
    {
        stepIndex = 0;
        if (deliveredCountIncludingCurrent <= 0 || deliveredCountIncludingCurrent % interval != 0)
            return false;

        var block = deliveredCountIncludingCurrent / interval;
        stepIndex = ((block - 1) % cycleLength) + 1;
        return true;
    }
}
