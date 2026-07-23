using SenorArroz.Application.Features.BranchPrintSettings.DTOs;
using SenorArroz.Domain.Enums;

namespace SenorArroz.API.Services;

public interface IPrintAgentNotificationService
    : SenorArroz.Application.Common.Interfaces.IPrintAgentNotifier
{
    Task NotifyConfigChangedAsync(int branchId, PrintAgentConfigDto config, CancellationToken cancellationToken = default);
    Task NotifyPrintJobsAvailableAsync(
        int branchId,
        long jobId,
        PrintJobKind kind,
        CancellationToken cancellationToken = default);
}
