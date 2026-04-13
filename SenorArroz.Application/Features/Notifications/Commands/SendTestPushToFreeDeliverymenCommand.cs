using MediatR;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Application.Features.Notifications.Commands;

public class SendTestPushToFreeDeliverymenCommand : IRequest<SendTestPushToFreeDeliverymenResultDto>
{
    /// <summary>Sucursal (obligatoria para Superadmin; ignorada para Admin si no coincide).</summary>
    public int? BranchId { get; set; }
}

public sealed record SendTestPushToFreeDeliverymenResultDto(
    int BranchId,
    int TokensTargeted,
    int BusyDeliverymanCount,
    string CorrelationId);

public class SendTestPushToFreeDeliverymenHandler : IRequestHandler<SendTestPushToFreeDeliverymenCommand, SendTestPushToFreeDeliverymenResultDto>
{
    private const string LogPrefix = "FCM_TEST";

    private readonly IFreeDeliverymanFcmTokenResolver _tokenResolver;
    private readonly IFcmPushService _fcm;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<SendTestPushToFreeDeliverymenHandler> _logger;

    public SendTestPushToFreeDeliverymenHandler(
        IFreeDeliverymanFcmTokenResolver tokenResolver,
        IFcmPushService fcm,
        ICurrentUser currentUser,
        ILogger<SendTestPushToFreeDeliverymenHandler> logger)
    {
        _tokenResolver = tokenResolver;
        _fcm = fcm;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<SendTestPushToFreeDeliverymenResultDto> Handle(
        SendTestPushToFreeDeliverymenCommand request,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..12];
        var role = _currentUser.Role?.Trim() ?? string.Empty;
        var isSuperadmin = Roles.IsSuperadmin(role);

        _logger.LogInformation(
            "{Prefix} [{Corr}] STEP auth_ok userId={UserId} role={Role} requestBranchId={RequestBranch}",
            LogPrefix, correlationId, _currentUser.Id, role, request.BranchId);

        int branchId;
        if (isSuperadmin)
        {
            if (request.BranchId is null or <= 0)
            {
                _logger.LogWarning(
                    "{Prefix} [{Corr}] STEP fail superadmin_missing_branch",
                    LogPrefix, correlationId);
                throw new InvalidOperationException(
                    "Superadmin: indique sucursal (branchId) en el cuerpo de la petición.");
            }

            branchId = request.BranchId.Value;
        }
        else
        {
            branchId = _currentUser.BranchId;
            if (branchId <= 0)
            {
                _logger.LogWarning(
                    "{Prefix} [{Corr}] STEP fail admin_no_branch userId={UserId}",
                    LogPrefix, correlationId, _currentUser.Id);
                throw new InvalidOperationException(
                    "Tu usuario no tiene sucursal asignada; no se puede enviar la prueba.");
            }

            if (request.BranchId is > 0 && request.BranchId.Value != branchId)
            {
                _logger.LogWarning(
                    "{Prefix} [{Corr}] STEP fail branch_mismatch userBranch={UserBranch} requestBranch={RequestBranch}",
                    LogPrefix, correlationId, branchId, request.BranchId);
                throw new InvalidOperationException(
                    "No puede probar notificaciones de otra sucursal.");
            }
        }

        _logger.LogInformation(
            "{Prefix} [{Corr}] STEP resolve_tokens branchId={BranchId}",
            LogPrefix, correlationId, branchId);

        var resolved = await _tokenResolver.ResolveAsync(branchId, cancellationToken);

        _logger.LogInformation(
            "{Prefix} [{Corr}] STEP tokens_resolved count={Count} busyDeliverymen={Busy}",
            LogPrefix, correlationId, resolved.Tokens.Count, resolved.BusyDeliverymanCount);

        if (resolved.Tokens.Count == 0)
        {
            _logger.LogInformation(
                "{Prefix} [{Corr}] STEP skip_send no_tokens branchId={BranchId}",
                LogPrefix, correlationId, branchId);
            return new SendTestPushToFreeDeliverymenResultDto(
                branchId,
                0,
                resolved.BusyDeliverymanCount,
                correlationId);
        }

        _logger.LogInformation(
            "{Prefix} [{Corr}] STEP fcm_send_start tokens={Count}",
            LogPrefix, correlationId, resolved.Tokens.Count);

        try
        {
            await _fcm.SendToTokensAsync(
                resolved.Tokens,
                title: "Prueba de notificación",
                body: "Mensaje de prueba desde administración (domiciliarios libres).",
                data: new Dictionary<string, string>
                {
                    ["type"] = "push_test",
                    ["branchId"] = branchId.ToString(),
                },
                cancellationToken,
                correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "{Prefix} [{Corr}] STEP fcm_send_exception",
                LogPrefix, correlationId);
            throw;
        }

        _logger.LogInformation(
            "{Prefix} [{Corr}] STEP fcm_send_finished tokens={Count}",
            LogPrefix, correlationId, resolved.Tokens.Count);

        return new SendTestPushToFreeDeliverymenResultDto(
            branchId,
            resolved.Tokens.Count,
            resolved.BusyDeliverymanCount,
            correlationId);
    }
}
