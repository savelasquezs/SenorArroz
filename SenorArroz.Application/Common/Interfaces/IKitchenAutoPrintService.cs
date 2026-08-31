using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Interfaces;

public interface IKitchenAutoPrintService
{
    Task<bool> TryEnqueueAsync(
        Order order,
        KitchenAutoPrintTrigger requiredTrigger,
        CancellationToken cancellationToken = default);
}
