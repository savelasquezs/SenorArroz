using SenorArroz.Application.Features.BranchPrintSettings.DTOs;

namespace SenorArroz.API.Services;

public interface IPrintAgentNotificationService
    : SenorArroz.Application.Common.Interfaces.IPrintAgentNotifier
{
    Task NotifyConfigChangedAsync(int branchId, PrintAgentConfigDto config, CancellationToken cancellationToken = default);
    Task NotifyPrintJobsAvailableAsync(int branchId, CancellationToken cancellationToken = default);
}
