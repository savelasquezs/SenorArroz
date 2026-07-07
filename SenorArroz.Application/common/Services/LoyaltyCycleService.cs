using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Common.Services;

public class LoyaltyCycleService : ILoyaltyCycleService
{
    private readonly IOrderRepository _orders;
    private readonly ILoyaltyCycleStepRepository _steps;

    public LoyaltyCycleService(IOrderRepository orders, ILoyaltyCycleStepRepository steps)
    {
        _orders = orders;
        _steps = steps;
    }

    public async Task ApplyLoyaltyPreviewToCustomerDtoAsync(CustomerDto dto, CancellationToken cancellationToken = default)
    {
        var cycleLen = await _steps.GetCycleLengthAsync(dto.BranchId, cancellationToken);
        if (cycleLen <= 0)
        {
            dto.LoyaltyDeliveredCount = await _orders.CountDeliveredOrdersForCustomerAsync(dto.Id, cancellationToken);
            dto.LoyaltyNextStepIndex = null;
            dto.LoyaltyNextRewardLabel = null;
            dto.LoyaltyDeliveriesUntilNextReward = null;
            dto.LoyaltyRewardDueOnCurrentOrder = false;
            dto.LoyaltyNextRewardMessage = null;
            return;
        }

        var delivered = await _orders.CountDeliveredOrdersForCustomerAsync(dto.Id, cancellationToken);
        dto.LoyaltyDeliveredCount = delivered;
        var nextMilestone = LoyaltyDeliveriesPerReward.GetNextRewardMilestoneDeliveries(delivered);
        var nextStepIndex = LoyaltyDeliveriesPerReward.GetStepIndexAtMilestone(nextMilestone, cycleLen);
        dto.LoyaltyNextStepIndex = nextStepIndex;
        var step = await _steps.GetByBranchAndStepIndexAsync(dto.BranchId, nextStepIndex, cancellationToken);
        dto.LoyaltyNextRewardLabel = step?.RewardLabel;
        if (!string.IsNullOrWhiteSpace(step?.RewardLabel))
        {
            var falta = LoyaltyDeliveriesPerReward.GetDeliveriesUntilNextReward(delivered);
            dto.LoyaltyDeliveriesUntilNextReward = falta;
            dto.LoyaltyRewardDueOnCurrentOrder = falta == 1;
            dto.LoyaltyNextRewardMessage =
                $"Este cliente tiene {delivered} pedido(s) entregado(s) con nosotros. Le faltan {falta} entrega(s) para el premio: {step!.RewardLabel}.";
        }
        else
        {
            dto.LoyaltyDeliveriesUntilNextReward = null;
            dto.LoyaltyRewardDueOnCurrentOrder = false;
            dto.LoyaltyNextRewardMessage = null;
        }
    }

    public async Task OnOrderDeliveredAsync(int orderId, int branchId, int? customerId, CancellationToken cancellationToken = default)
    {
        if (!customerId.HasValue)
            return;

        var cycleLen = await _steps.GetCycleLengthAsync(branchId, cancellationToken);
        if (cycleLen <= 0)
            return;

        var k = await _orders.CountDeliveredOrdersForCustomerAsync(customerId.Value, cancellationToken);
        if (k <= 0)
            return;

        if (!LoyaltyDeliveriesPerReward.TryGetStepIndexForDeliveredCount(
                k,
                cycleLen,
                LoyaltyDeliveriesPerReward.DefaultInterval,
                out var stepIndex))
            return;

        var step = await _steps.GetByBranchAndStepIndexAsync(branchId, stepIndex, cancellationToken);
        if (step == null)
            return;

        await _orders.UpdateOrderLoyaltyCycleAsync(orderId, step.Id, step.RewardLabel, cancellationToken);
    }

    public Task OnOrderLeftDeliveredAsync(int orderId, CancellationToken cancellationToken = default) =>
        _orders.UpdateOrderLoyaltyCycleAsync(orderId, null, null, cancellationToken);
}
