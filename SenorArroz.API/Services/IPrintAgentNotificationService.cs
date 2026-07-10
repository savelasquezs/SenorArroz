using SenorArroz.Application.Features.BranchPrintSettings.DTOs;

namespace SenorArroz.API.Services;

public interface IPrintAgentNotificationService
{
    Task NotifyConfigChangedAsync(int branchId, PrintAgentConfigDto config, CancellationToken cancellationToken = default);
}
