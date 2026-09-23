using Microsoft.AspNetCore.SignalR;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.API.Hubs;

public class PrintAgentHub : Hub
{
    private readonly IPrintQueueService _printQueue;
    private readonly ILogger<PrintAgentHub> _logger;
    private readonly ITenantExecutionContext _tenantExecutionContext;

    public PrintAgentHub(IPrintQueueService printQueue, ITenantExecutionContext tenantExecutionContext, ILogger<PrintAgentHub> logger)
    {
        _printQueue = printQueue;
        _tenantExecutionContext = tenantExecutionContext;
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

        var identity = await _printQueue.AuthenticateAgentAsync(branchId, token, Context.ConnectionAborted);
        if (identity is null)
        {
            _logger.LogWarning("PrintAgentHub connection rejected: invalid token for branch {BranchId}.", branchId);
            Context.Abort();
            return;
        }

        using var tenantScope = _tenantExecutionContext.BeginTenantScope(identity.TenantId);
        Context.Items[nameof(PrintAgentIdentity)] = identity;
        await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(identity.TenantId, branchId), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(nameof(PrintAgentIdentity), out var value) && value is PrintAgentIdentity identity)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(identity.TenantId, identity.BranchId), Context.ConnectionAborted);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public static string GetGroupName(int tenantId, int branchId) => TenantRealtimeGroups.BranchRole(tenantId, branchId, "PrintAgent");
}
