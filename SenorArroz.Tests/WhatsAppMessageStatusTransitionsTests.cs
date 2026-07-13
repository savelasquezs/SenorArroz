using SenorArroz.API.Services;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Tests;

public class WhatsAppMessageStatusTransitionsTests
{
    [Fact]
    public void DuplicateStatus_IsIgnored() =>
        Assert.False(WhatsAppMessageStatusTransitions.ShouldApply(
            WhatsAppMessageStatus.Failed,
            WhatsAppMessageStatus.Failed));

    [Theory]
    [InlineData(WhatsAppMessageStatus.Delivered)]
    [InlineData(WhatsAppMessageStatus.Read)]
    public void StaleFailure_DoesNotRegressDeliveryProof(WhatsAppMessageStatus current) =>
        Assert.False(WhatsAppMessageStatusTransitions.ShouldApply(
            current,
            WhatsAppMessageStatus.Failed));

    [Theory]
    [InlineData(WhatsAppMessageStatus.Delivered)]
    [InlineData(WhatsAppMessageStatus.Read)]
    public void DeliveryProof_CanHealPreviousFailure(WhatsAppMessageStatus incoming) =>
        Assert.True(WhatsAppMessageStatusTransitions.ShouldApply(
            WhatsAppMessageStatus.Failed,
            incoming));

    [Fact]
    public void Delivered_CanAdvanceToRead() =>
        Assert.True(WhatsAppMessageStatusTransitions.ShouldApply(
            WhatsAppMessageStatus.Delivered,
            WhatsAppMessageStatus.Read));
}
