using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Interfaces;
public interface IWhatsAppAiTelemetryQueue
{
    bool TryEnqueue(WhatsAppAiInvocation invocation);
}
