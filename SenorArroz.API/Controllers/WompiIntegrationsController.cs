using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Authorize(Roles = "Superadmin, Admin")]
public sealed class WompiIntegrationsController(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IIntegrationSecretProtector protector,
    IWompiPaymentService wompi,
    IClock clock,
    IOptions<StorefrontCustomerAuthOptions> storefrontOptions) : ControllerBase
{
    [HttpGet("api/branches/{branchId:int}/payment-integrations/wompi")]
    public async Task<ActionResult<ApiResponse<object>>> Get(int branchId, CancellationToken cancellationToken)
    {
        if (!CanAdminister(branchId)) return Forbid();
        var integration = await db.WompiPaymentIntegrations.AsNoTracking()
            .Include(x => x.FinancialApp)
            .FirstOrDefaultAsync(x => x.TenantId == TenantId && x.BranchId == branchId, cancellationToken);
        var apps = await db.Apps.AsNoTracking()
            .Where(x => x.Active && x.Bank.Active && x.Bank.BranchId == branchId)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, bankName = x.Bank.Name })
            .ToListAsync(cancellationToken);
        return Ok(ApiResponse<object>.SuccessResponse(new
        {
            integration = integration is null ? null : ToDto(integration),
            financialApps = apps,
        }));
    }

    [HttpPut("api/branches/{branchId:int}/payment-integrations/wompi")]
    public async Task<ActionResult<ApiResponse<object>>> Upsert(
        int branchId,
        [FromBody] UpsertWompiIntegrationDto dto,
        CancellationToken cancellationToken)
    {
        if (!CanAdminister(branchId)) return Forbid();
        if (dto.Sandbox is null || dto.Production is null)
            return BadRequest(ApiResponse<object>.ErrorResponse("Debes enviar la configuración de Sandbox y Producción."));
        if (!await db.Branches.AnyAsync(x => x.Id == branchId, cancellationToken)) return NotFound();
        if (!await db.Apps.AnyAsync(x => x.Id == dto.FinancialAppId
            && x.Active
            && x.Bank.Active
            && x.Bank.BranchId == branchId, cancellationToken))
            return BadRequest(ApiResponse<object>.ErrorResponse("Selecciona una App de pago activa de esta sucursal."));
        if (dto.EstimatedCommissionRate is < 0 or > 1)
            return BadRequest(ApiResponse<object>.ErrorResponse("La comisión estimada debe estar entre 0 y 1."));
        var environment = NormalizeEnvironment(dto.ActiveEnvironment);
        if (environment is null)
            return BadRequest(ApiResponse<object>.ErrorResponse("El ambiente debe ser Sandbox o Producción."));

        var integration = await db.WompiPaymentIntegrations
            .FirstOrDefaultAsync(x => x.TenantId == TenantId && x.BranchId == branchId, cancellationToken);
        var isNew = integration is null;
        integration ??= new WompiPaymentIntegration { TenantId = TenantId, BranchId = branchId };
        if (integration.Id == 0) db.WompiPaymentIntegrations.Add(integration);
        integration.FinancialAppId = dto.FinancialAppId;
        integration.ActiveEnvironment = environment;
        integration.IsEnabled = dto.IsEnabled;
        integration.EstimatedCommissionRate = dto.EstimatedCommissionRate;
        ApplyEnvironmentCredentials(integration, "sandbox", dto.Sandbox);
        ApplyEnvironmentCredentials(integration, "production", dto.Production);

        if (dto.IsEnabled)
        {
            var credentials = Credentials(integration, environment);
            if (credentials.PublicKey is null || credentials.Integrity is null || credentials.Events is null)
                return BadRequest(ApiResponse<object>.ErrorResponse($"Completa las tres credenciales de {EnvironmentLabel(environment)} antes de activar Wompi."));
            try
            {
                ValidatePrefixes(environment, credentials.PublicKey, protector.Unprotect(credentials.Integrity), protector.Unprotect(credentials.Events));
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.Security.Cryptography.CryptographicException)
            {
                return BadRequest(ApiResponse<object>.ErrorResponse(exception.Message));
            }
        }

        integration.LastError = null;
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync(integration, isNew ? "CREATED" : "UPDATED", cancellationToken);
        return Ok(ApiResponse<object>.SuccessResponse(ToDto(integration), "Configuración Wompi guardada."));
    }

    [HttpPost("api/branches/{branchId:int}/payment-integrations/wompi/test")]
    public async Task<ActionResult<ApiResponse<object>>> Test(int branchId, CancellationToken cancellationToken)
    {
        if (!CanAdminister(branchId)) return Forbid();
        var integration = await db.WompiPaymentIntegrations
            .FirstOrDefaultAsync(x => x.TenantId == TenantId && x.BranchId == branchId, cancellationToken);
        if (integration is null)
            return NotFound(ApiResponse<object>.ErrorResponse("Configura Wompi antes de probarlo."));
        var credentials = Credentials(integration, integration.ActiveEnvironment);
        if (credentials.PublicKey is null || credentials.Integrity is null || credentials.Events is null)
            return BadRequest(ApiResponse<object>.ErrorResponse("La configuración del ambiente activo está incompleta."));
        try
        {
            ValidatePrefixes(integration.ActiveEnvironment, credentials.PublicKey, protector.Unprotect(credentials.Integrity), protector.Unprotect(credentials.Events));
            if (!await wompi.TestPublicKeyAsync(integration.ActiveEnvironment, credentials.PublicKey, cancellationToken))
                throw new InvalidOperationException("Wompi no reconoció la llave pública.");
            integration.LastTestedAt = clock.UtcNow;
            integration.LastError = null;
            await db.SaveChangesAsync(cancellationToken);
            await AuditAsync(integration, "TESTED", cancellationToken);
            return Ok(ApiResponse<object>.SuccessResponse(ToDto(integration), "Llave pública validada. El secreto de eventos se verificará con el primer webhook firmado."));
        }
        catch (Exception exception)
        {
            integration.LastError = exception.Message.Length > 1000 ? exception.Message[..1000] : exception.Message;
            await db.SaveChangesAsync(cancellationToken);
            return BadRequest(ApiResponse<object>.ErrorResponse(integration.LastError));
        }
    }

    [HttpGet("api/payments/wompi/reviews")]
    public async Task<ActionResult<ApiResponse<object>>> GetReviews([FromQuery] int? branchId, CancellationToken cancellationToken)
    {
        var effectiveBranch = ResolveBranch(branchId);
        if (!effectiveBranch.HasValue) return BadRequest(ApiResponse<object>.ErrorResponse("Selecciona una sucursal."));
        var reviews = await db.WompiPaymentAttempts.AsNoTracking()
            .Where(x => x.TenantId == TenantId && x.Order.BranchId == effectiveBranch && x.RequiresManualReview)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.OrderId,
                x.Reference,
                amount = x.ExpectedAmountInCents / 100m,
                x.ManualReviewReason,
                x.ApprovedAt,
                x.ExpiresAt,
                x.CreatedAt,
                canApprove = x.Order.Status == OrderStatus.AwaitingPayment && x.AppPaymentId == null,
            })
            .ToListAsync(cancellationToken);
        return Ok(ApiResponse<object>.SuccessResponse(reviews));
    }

    [HttpPost("api/payments/wompi/reviews/{attemptId:int}/resolve")]
    public async Task<ActionResult<ApiResponse<object>>> ResolveReview(
        int attemptId,
        [FromBody] ResolveWompiReviewDto dto,
        CancellationToken cancellationToken)
    {
        var branchId = await db.WompiPaymentAttempts.AsNoTracking()
            .Where(x => x.Id == attemptId && x.TenantId == TenantId)
            .Select(x => (int?)x.Order.BranchId)
            .FirstOrDefaultAsync(cancellationToken);
        if (!branchId.HasValue) return NotFound();
        if (!CanAdminister(branchId.Value)) return Forbid();
        var result = await wompi.ResolveManualReviewAsync(attemptId, currentUser.Id, dto.Approve, clock.UtcNow, cancellationToken);
        return Ok(ApiResponse<object>.SuccessResponse(result, dto.Approve ? "Pago aprobado y pedido enviado a cocina." : "Pago dejado fuera del flujo operativo."));
    }

    private int TenantId => Math.Max(1, storefrontOptions.Value.TenantId);
    private bool CanAdminister(int branchId) => currentUser.Role.Equals("superadmin", StringComparison.OrdinalIgnoreCase)
        || currentUser.Role.Equals("admin", StringComparison.OrdinalIgnoreCase) && currentUser.BranchId == branchId;
    private int? ResolveBranch(int? requested) => currentUser.Role.Equals("superadmin", StringComparison.OrdinalIgnoreCase)
        ? requested
        : currentUser.BranchId;

    private async Task AuditAsync(WompiPaymentIntegration integration, string action, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational()) return;
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO wompi_integration_audit (tenant_id, branch_id, integration_id, action, user_id)
            VALUES ({integration.TenantId}, {integration.BranchId}, {integration.Id}, {action}, {currentUser.Id})", cancellationToken);
    }

    private void ApplyEnvironmentCredentials(WompiPaymentIntegration integration, string environment, WompiEnvironmentCredentialsDto dto)
    {
        var publicKey = Clean(dto.PublicKey);
        var integrity = Clean(dto.IntegritySecret);
        var events = Clean(dto.EventsSecret);
        if (environment == "sandbox")
        {
            if (publicKey is not null) integration.SandboxPublicKey = publicKey;
            if (integrity is not null) integration.SandboxEncryptedIntegritySecret = protector.Protect(integrity);
            if (events is not null) integration.SandboxEncryptedEventsSecret = protector.Protect(events);
        }
        else
        {
            if (publicKey is not null) integration.ProductionPublicKey = publicKey;
            if (integrity is not null) integration.ProductionEncryptedIntegritySecret = protector.Protect(integrity);
            if (events is not null) integration.ProductionEncryptedEventsSecret = protector.Protect(events);
        }
    }

    private static (string? PublicKey, string? Integrity, string? Events) Credentials(WompiPaymentIntegration integration, string environment) =>
        environment == "production"
            ? (integration.ProductionPublicKey, integration.ProductionEncryptedIntegritySecret, integration.ProductionEncryptedEventsSecret)
            : (integration.SandboxPublicKey, integration.SandboxEncryptedIntegritySecret, integration.SandboxEncryptedEventsSecret);

    private static object ToDto(WompiPaymentIntegration integration) => new
    {
        integration.Id,
        integration.BranchId,
        integration.FinancialAppId,
        financialAppName = integration.FinancialApp?.Name,
        integration.ActiveEnvironment,
        integration.IsEnabled,
        integration.EstimatedCommissionRate,
        sandbox = new
        {
            publicKey = integration.SandboxPublicKey,
            integritySecretConfigured = integration.SandboxEncryptedIntegritySecret is not null,
            eventsSecretConfigured = integration.SandboxEncryptedEventsSecret is not null,
            lastWebhookAt = integration.LastSandboxWebhookAt,
        },
        production = new
        {
            publicKey = integration.ProductionPublicKey,
            integritySecretConfigured = integration.ProductionEncryptedIntegritySecret is not null,
            eventsSecretConfigured = integration.ProductionEncryptedEventsSecret is not null,
            lastWebhookAt = integration.LastProductionWebhookAt,
        },
        integration.LastTestedAt,
        integration.LastError,
    };

    private static void ValidatePrefixes(string environment, string publicKey, string integrity, string events)
    {
        var production = environment == "production";
        if (!publicKey.StartsWith(production ? "pub_prod_" : "pub_test_", StringComparison.Ordinal)
            || !integrity.StartsWith(production ? "prod_integrity_" : "test_integrity_", StringComparison.Ordinal)
            || !events.StartsWith(production ? "prod_events_" : "test_events_", StringComparison.Ordinal))
            throw new InvalidOperationException("Las credenciales no corresponden al ambiente seleccionado.");
    }

    private static string? NormalizeEnvironment(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "sandbox" => "sandbox",
        "production" => "production",
        _ => null,
    };
    private static string EnvironmentLabel(string value) => value == "production" ? "Producción" : "Sandbox";
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class UpsertWompiIntegrationDto
{
    public int FinancialAppId { get; set; }
    public string ActiveEnvironment { get; set; } = "sandbox";
    public bool IsEnabled { get; set; }
    public decimal EstimatedCommissionRate { get; set; }
    public WompiEnvironmentCredentialsDto Sandbox { get; set; } = new();
    public WompiEnvironmentCredentialsDto Production { get; set; } = new();
}

public sealed class WompiEnvironmentCredentialsDto
{
    public string? PublicKey { get; set; }
    public string? IntegritySecret { get; set; }
    public string? EventsSecret { get; set; }
}

public sealed class ResolveWompiReviewDto
{
    public bool Approve { get; set; }
}
