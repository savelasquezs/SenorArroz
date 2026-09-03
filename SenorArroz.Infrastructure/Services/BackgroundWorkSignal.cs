using System.Threading.Channels;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Services;

public sealed class BackgroundWorkSignal<TWork> : IBackgroundWorkSignal<TWork> where TWork : class
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });

    public void Pulse() => _channel.Writer.TryWrite(true);

    public async ValueTask<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
            return _channel.Reader.TryRead(out _);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            if (!await _channel.Reader.WaitToReadAsync(timeoutSource.Token))
                return false;
            while (_channel.Reader.TryRead(out _)) { }
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
