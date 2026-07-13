using System.Threading.Channels;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;

namespace SenorArroz.API.Services;

public sealed class WhatsAppAiTelemetryQueue(ILogger<WhatsAppAiTelemetryQueue> logger) : IWhatsAppAiTelemetryQueue
{
    private readonly Channel<WhatsAppAiInvocation> _channel = Channel.CreateBounded<WhatsAppAiInvocation>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
    public ChannelReader<WhatsAppAiInvocation> Reader => _channel.Reader;
    public bool TryEnqueue(WhatsAppAiInvocation invocation)
    {
        var accepted = _channel.Writer.TryWrite(invocation);
        if (!accepted) logger.LogWarning("WhatsApp AI telemetry queue is full; invocation dropped Provider={Provider} Model={Model}", invocation.Provider, invocation.Model);
        return accepted;
    }
    public void Complete() => _channel.Writer.TryComplete();
}
