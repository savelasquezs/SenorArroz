namespace SenorArroz.Application.Common.Printing;

public sealed record LoyaltyKitchenSnapshot(
    int DeliveredCount,
    int? OrdersUntilCycleEnd,
    string? NextRewardLabel,
    string? ThisOrderGiftLabel);
