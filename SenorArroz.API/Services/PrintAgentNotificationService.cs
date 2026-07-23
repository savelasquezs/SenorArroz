using Microsoft.AspNetCore.SignalR;
using SenorArroz.API.Hubs;
using SenorArroz.Application.Features.BranchPrintSettings.DTOs;
using SenorArroz.Domain.Enums;

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

    public async Task NotifyJobsAvailableAsync(
        int branchId,
        long jobId,
        PrintJobKind kind,
        CancellationToken cancellationToken = default)
    {
        var notification = new PrintJobsAvailableNotification(
            jobId,
            branchId,
            KindToApiString(kind));

        await _hubContext.Clients.Group(PrintAgentHub.GetGroupName(branchId))
            .SendAsync("PrintJobsAvailable", notification, cancellationToken);

        _logger.LogInformation(
            "Print job notification pushed. PrintJobId={PrintJobId} BranchId={BranchId} Kind={PrintJobKind}.",
            jobId,
            branchId,
            notification.Kind);
    }

    public Task NotifyPrintJobsAvailableAsync(
        int branchId,
        long jobId,
        PrintJobKind kind,
        CancellationToken cancellationToken = default)
        => NotifyJobsAvailableAsync(branchId, jobId, kind, cancellationToken);

    private static string KindToApiString(PrintJobKind kind) => kind switch
    {
        PrintJobKind.Kitchen => "kitchen",
        PrintJobKind.Delivery => "delivery",
        PrintJobKind.Cashier => "cashier",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

public record PrintJobsAvailableNotification(long JobId, int BranchId, string Kind);
