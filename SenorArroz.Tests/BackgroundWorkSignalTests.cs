using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public sealed class BackgroundWorkSignalTests
{
    [Fact]
    public async Task Pulse_WakesWaitingWorker()
    {
        var signal = new BackgroundWorkSignal<RappiWork>();
        signal.Pulse();

        var awakened = await signal.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.True(awakened);
    }

    [Fact]
    public async Task MultiplePulses_AreCoalesced()
    {
        var signal = new BackgroundWorkSignal<EmailOutboxWork>();
        signal.Pulse();
        signal.Pulse();
        signal.Pulse();

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None));
        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(20), CancellationToken.None));
    }

    [Fact]
    public async Task WaitAsync_ReturnsFalseAfterTimeout()
    {
        var signal = new BackgroundWorkSignal<PaymentNotificationOutboxWork>();

        var awakened = await signal.WaitAsync(TimeSpan.FromMilliseconds(20), CancellationToken.None);

        Assert.False(awakened);
    }
}
