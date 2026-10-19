using Moq;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Tests;

public sealed class CustomerLoyaltyBatchTests
{
    [Fact]
    public async Task Batch_preview_reuses_steps_across_branches_without_customer_count_queries()
    {
        var orders = new Mock<IOrderRepository>(MockBehavior.Strict);
        var steps = new Mock<ILoyaltyCycleStepRepository>(MockBehavior.Strict);
        IReadOnlyList<LoyaltyCycleStep> activeSteps = new[]
        {
            new LoyaltyCycleStep { BranchId = 1, StepIndex = 1, RewardLabel = "Yuca" },
            new LoyaltyCycleStep { BranchId = 1, StepIndex = 2, RewardLabel = "Papas" }
        };
        steps.Setup(s => s.GetActiveStepsForBranchesAsync(
                It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeSteps);

        var first = new CustomerDto { Id = 10, BranchId = 1, LoyaltyDeliveredCount = 4 };
        var second = new CustomerDto { Id = 11, BranchId = 1, LoyaltyDeliveredCount = 5 };
        var withoutCycle = new CustomerDto { Id = 12, BranchId = 2, LoyaltyDeliveredCount = 2 };

        await new LoyaltyCycleService(orders.Object, steps.Object)
            .ApplyLoyaltyPreviewsToCustomerDtosAsync(new[] { first, second, withoutCycle });

        Assert.Equal(4, first.LoyaltyDeliveredCount);
        Assert.Equal(1, first.LoyaltyNextStepIndex);
        Assert.Equal("Yuca", first.LoyaltyNextRewardLabel);
        Assert.Equal(1, first.LoyaltyDeliveriesUntilNextReward);
        Assert.True(first.LoyaltyRewardDueOnCurrentOrder);

        Assert.Equal(2, second.LoyaltyNextStepIndex);
        Assert.Equal("Papas", second.LoyaltyNextRewardLabel);
        Assert.Equal(5, second.LoyaltyDeliveriesUntilNextReward);
        Assert.False(second.LoyaltyRewardDueOnCurrentOrder);

        Assert.Equal(2, withoutCycle.LoyaltyDeliveredCount);
        Assert.Null(withoutCycle.LoyaltyNextStepIndex);
        Assert.Null(withoutCycle.LoyaltyNextRewardMessage);

        steps.Verify(s => s.GetActiveStepsForBranchesAsync(
            It.Is<IReadOnlyCollection<int>>(ids => ids.Count == 2 && ids.Contains(1) && ids.Contains(2)),
            It.IsAny<CancellationToken>()), Times.Once);
        orders.VerifyNoOtherCalls();
        steps.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Single_and_bulk_previews_return_same_reward()
    {
        var orders = new Mock<IOrderRepository>(MockBehavior.Strict);
        var steps = new Mock<ILoyaltyCycleStepRepository>(MockBehavior.Strict);
        var step = new LoyaltyCycleStep { BranchId = 1, StepIndex = 1, RewardLabel = "Yuca" };
        orders.Setup(o => o.CountDeliveredOrdersForCustomerAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(4);
        steps.Setup(s => s.GetCycleLengthAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        steps.Setup(s => s.GetByBranchAndStepIndexAsync(1, 1, It.IsAny<CancellationToken>())).ReturnsAsync(step);
        steps.Setup(s => s.GetActiveStepsForBranchesAsync(
                It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { step } as IReadOnlyList<LoyaltyCycleStep>);

        var service = new LoyaltyCycleService(orders.Object, steps.Object);
        var single = new CustomerDto { Id = 10, BranchId = 1 };
        var bulk = new CustomerDto { Id = 10, BranchId = 1, LoyaltyDeliveredCount = 4 };

        await service.ApplyLoyaltyPreviewToCustomerDtoAsync(single);
        await service.ApplyLoyaltyPreviewsToCustomerDtosAsync(new[] { bulk });

        Assert.Equal(single.LoyaltyDeliveredCount, bulk.LoyaltyDeliveredCount);
        Assert.Equal(single.LoyaltyNextStepIndex, bulk.LoyaltyNextStepIndex);
        Assert.Equal(single.LoyaltyNextRewardLabel, bulk.LoyaltyNextRewardLabel);
        Assert.Equal(single.LoyaltyDeliveriesUntilNextReward, bulk.LoyaltyDeliveriesUntilNextReward);
        Assert.Equal(single.LoyaltyRewardDueOnCurrentOrder, bulk.LoyaltyRewardDueOnCurrentOrder);
        Assert.Equal(single.LoyaltyNextRewardMessage, bulk.LoyaltyNextRewardMessage);
    }

    [Fact]
    public async Task Empty_page_does_not_fetch_loyalty_steps()
    {
        var orders = new Mock<IOrderRepository>(MockBehavior.Strict);
        var steps = new Mock<ILoyaltyCycleStepRepository>(MockBehavior.Strict);
        await new LoyaltyCycleService(orders.Object, steps.Object)
            .ApplyLoyaltyPreviewsToCustomerDtosAsync(Array.Empty<CustomerDto>());
        orders.VerifyNoOtherCalls();
        steps.VerifyNoOtherCalls();
    }
}
