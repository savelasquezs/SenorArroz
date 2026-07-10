using Microsoft.AspNetCore.SignalR;
using SenorArroz.API.Hubs;
using SenorArroz.Application.Features.BranchPrintSettings.DTOs;

namespace SenorArroz.API.Services;

public class PrintAgentNotificationService : IPrintAgentNotificationService
{
    private readonly IHubContext<PrintAgentHub> _hubContext;
    private readonly ILogger<PrintAgentNotificationService> _logger;

    public PrintAgentNotificationService(IHubContext<PrintAgentHub> hubContext, ILogger<PrintAgentNotificationService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyConfigChangedAsync(int branchId, PrintAgentConfigDto config, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group(PrintAgentHub.GetGroupName(branchId))
            .SendAsync("PrintAgentConfigChanged", config, cancellationToken);

        _logger.LogInformation("PrintAgent config pushed to branch {BranchId}.", branchId);
    }

    public async Task NotifyPrintJobsAvailableAsync(int branchId, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group(PrintAgentHub.GetGroupName(branchId))
            .SendAsync("PrintJobsAvailable", new { branchId }, cancellationToken);

        _logger.LogInformation("Print jobs availability pushed to branch {BranchId}.", branchId);
    }
}
