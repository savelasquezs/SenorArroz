using Microsoft.AspNetCore.SignalR;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.API.Hubs;

public class PrintAgentHub : Hub
{
    private readonly IPrintQueueService _printQueue;
    private readonly ILogger<PrintAgentHub> _logger;

    public PrintAgentHub(IPrintQueueService printQueue, ILogger<PrintAgentHub> logger)
    {
        _printQueue = printQueue;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var branchIdRaw = Context.GetHttpContext()?.Request.Query["branchId"].FirstOrDefault();
        var token = Context.GetHttpContext()?.Request.Query["token"].FirstOrDefault();

        if (!int.TryParse(branchIdRaw, out var branchId) || string.IsNullOrWhiteSpace(token))
        {
            _logger.LogWarning("PrintAgentHub connection rejected: missing branchId/token.");
            Context.Abort();
            return;
        }

        if (!await _printQueue.IsAgentTokenValidAsync(branchId, token, Context.ConnectionAborted))
        {
            _logger.LogWarning("PrintAgentHub connection rejected: invalid token for branch {BranchId}.", branchId);
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(branchId), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var branchIdRaw = Context.GetHttpContext()?.Request.Query["branchId"].FirstOrDefault();
        if (int.TryParse(branchIdRaw, out var branchId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(branchId), Context.ConnectionAborted);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public static string GetGroupName(int branchId) => $"PrintAgent_{branchId}";
}
