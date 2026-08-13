using Microsoft.AspNetCore.SignalR;
using SenorArroz.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Constants;
using SenorArroz.Domain.Enums;

namespace SenorArroz.API.Hubs;

public class PrintAgentHub : Hub
{
    private readonly IPrintQueueService _printQueue;
    private readonly ILogger<PrintAgentHub> _logger;
    private readonly IApplicationDbContext _db;
    private readonly ITenantExecutionContext _tenantExecution;
    private readonly ITenantCapabilityService _capabilities;
    private readonly ITenantConnectionRegistry _connections;
    private IDisposable? _registration;

    public PrintAgentHub(
        IPrintQueueService printQueue,
        ILogger<PrintAgentHub> logger,
        IApplicationDbContext db,
        ITenantExecutionContext tenantExecution,
        ITenantCapabilityService capabilities,
        ITenantConnectionRegistry connections)
    {
        _printQueue = printQueue;
        _logger = logger;
        _db = db;
        _tenantExecution = tenantExecution;
        _capabilities = capabilities;
        _connections = connections;
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

        var tenant = await _db.Branches.AsNoTracking()
            .Where(x => x.Id == branchId && x.TenantId.HasValue)
            .Select(x => new { Id = x.TenantId!.Value, x.Tenant!.PublicId, x.Tenant.Status })
            .SingleOrDefaultAsync(Context.ConnectionAborted);
        if (tenant is null || tenant.Status != TenantStatus.Active)
        {
            Context.Abort();
            return;
        }

        using var tenantScope = _tenantExecution.BeginTenantScope(tenant.Id, tenant.PublicId);
        if (!await _capabilities.HasModuleAsync(TenantModules.Printing, Context.ConnectionAborted))
        {
            Context.Abort();
            return;
        }

        _registration = _connections.Register(tenant.Id, Context.ConnectionId, Context.Abort);
        await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(tenant.Id, branchId), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _registration?.Dispose();
        var branchIdRaw = Context.GetHttpContext()?.Request.Query["branchId"].FirstOrDefault();
        if (int.TryParse(branchIdRaw, out var branchId))
        {
            var tenantId = await _db.Branches.AsNoTracking().Where(x => x.Id == branchId).Select(x => x.TenantId).SingleOrDefaultAsync(Context.ConnectionAborted);
            if (tenantId.HasValue) await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(tenantId.Value, branchId), Context.ConnectionAborted);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public static string GetGroupName(int tenantId, int branchId) => $"Tenant_{tenantId}_PrintAgent_{branchId}";
}
