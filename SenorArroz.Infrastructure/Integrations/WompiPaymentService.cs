using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Infrastructure.Integrations;

public sealed class WompiPaymentService(
    IApplicationDbContext db,
    IIntegrationSecretProtector secrets,
    IClock clock,
    IPaymentReviewNotificationService reviewNotifications,
    HttpClient httpClient,
    ILogger<WompiPaymentService> logger) : IWompiPaymentService
{
    public Task<WompiPaymentIntegration?> GetEnabledIntegrationAsync(int tenantId, int branchId, CancellationToken cancellationToken) =>
        db.WompiPaymentIntegrations
            .Include(x => x.FinancialApp).ThenInclude(x => x.Bank)
            .FirstOrDefaultAsync(x => x.TenantId == tenantId
                && x.BranchId == branchId
                && x.IsEnabled
                && x.FinancialApp.Active
                && x.FinancialApp.Bank.Active
                && x.FinancialApp.Bank.BranchId == branchId,
                cancellationToken);

    public WompiCheckoutData CreateAttempt(Order order, WompiPaymentIntegration integration, DateTime utcNow)
    {
        if (order.Id <= 0)
            throw new BusinessException("El pedido debe existir antes de iniciar el pago.");
        if (order.Total <= 0)
            throw new BusinessException("El total del pedido no es válido para pago en línea.");

        var environment = NormalizeEnvironment(integration.ActiveEnvironment);
        var publicKey = GetPublicKey(integration, environment);
        var integritySecret = GetIntegritySecret(integration, environment);
        ValidateCredentialPrefixes(environment, publicKey, integritySecret, GetEventsSecret(integration, environment));

        var expiresAt = NormalizeUtc(utcNow).AddMinutes(15);
        var reference = $"SA-{Guid.NewGuid():N}".ToUpperInvariant();
        var amountInCents = checked((long)order.Total * 100L);
        var expiration = FormatExpiration(expiresAt);
        var signature = Sha256Hex($"{reference}{amountInCents}COP{expiration}{integritySecret}");
        var attempt = new WompiPaymentAttempt
        {
            TenantId = integration.TenantId,
            OrderId = order.Id,
            IntegrationId = integration.Id,
            Reference = reference,
            Environment = environment,
            PublicKeySnapshot = publicKey,
            IntegritySignature = signature,
            EncryptedEventsSecretSnapshot = GetEncryptedEventsSecret(integration, environment),
            ExpectedAmountInCents = amountInCents,
            Currency = "COP",
            Status = PaymentAttemptStatus.Pending,
            ExpiresAt = expiresAt,
        };
        db.WompiPaymentAttempts.Add(attempt);
        return ToCheckout(attempt);
    }

    public async Task<WompiWebhookProcessingResult> ProcessWebhookAsync(
        string environment,
        string rawPayload,
        string? headerChecksum,
        CancellationToken cancellationToken)
    {
        environment = NormalizeEnvironment(environment);
        using var document = JsonDocument.Parse(rawPayload);
        var root = document.RootElement;
        var transaction = root.GetProperty("data").GetProperty("transaction");
        var reference = RequiredString(transaction, "reference");
        var providerTransactionId = RequiredString(transaction, "id");
        var providerStatus = RequiredString(transaction, "status").ToUpperInvariant();
        var attempt = await db.WompiPaymentAttempts
            .Include(x => x.Integration)
            .Include(x => x.Order)
            .Include(x => x.ProviderTransactions)
            .FirstOrDefaultAsync(x => x.Reference == reference && x.Environment == environment, cancellationToken);
        if (attempt is null)
            return new(false, false, false, null, null, null, "Referencia desconocida.");

        var checksum = string.IsNullOrWhiteSpace(headerChecksum)
            ? root.GetProperty("signature").GetProperty("checksum").GetString()
            : headerChecksum;
        if (!ValidateEventChecksum(root, secrets.Unprotect(attempt.EncryptedEventsSecretSnapshot), checksum))
            return new(false, false, false, null, null, null, "Firma inválida.");

        var payloadHash = Sha256Hex(rawPayload);
        var timestamp = root.GetProperty("timestamp").GetRawText();
        var fingerprint = Sha256Hex($"{environment}|{timestamp}|{providerTransactionId}|{providerStatus}|{payloadHash}");
        if (await db.WompiWebhookEvents.AnyAsync(x => x.EventFingerprint == fingerprint, cancellationToken))
            return new(true, true, attempt.RequiresManualReview, attempt.Order.BranchId, attempt.OrderId, attempt.Id);

        var amountInCents = RequiredInt64(transaction, "amount_in_cents");
        var currency = RequiredString(transaction, "currency").ToUpperInvariant();
        if (amountInCents != attempt.ExpectedAmountInCents || currency != attempt.Currency)
            return new(false, false, false, null, null, null, "La transacción no coincide con el monto o moneda esperados.");

        var eventRow = new WompiWebhookEvent
        {
            TenantId = attempt.TenantId,
            IntegrationId = attempt.IntegrationId,
            Environment = environment,
            EventFingerprint = fingerprint,
            PayloadHash = payloadHash,
            ProviderTransactionId = providerTransactionId,
            Status = "processed",
            ProcessedAt = clock.UtcNow,
        };
        db.WompiWebhookEvents.Add(eventRow);
        var result = await ApplyTransactionObservationAsync(
            attempt,
            providerTransactionId,
            providerStatus,
            transaction.TryGetProperty("payment_method_type", out var method) ? method.GetString() : null,
            amountInCents,
            currency,
            payloadHash,
            clock.UtcNow,
            false,
            cancellationToken);
        if (environment == "sandbox") attempt.Integration.LastSandboxWebhookAt = clock.UtcNow;
        else attempt.Integration.LastProductionWebhookAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        if (result.RequiresManualReview)
            await NotifyReviewSafelyAsync(
                result.BranchId!.Value,
                result.OrderId!.Value,
                result.PaymentAttemptId!.Value,
                attempt.ManualReviewReason ?? "Pago que requiere revisión.",
                cancellationToken);
        return result;
    }

    public async Task<WompiPaymentStatusResult?> GetOrderPaymentStatusAsync(int tenantId, int orderId, CancellationToken cancellationToken)
    {
        var attempt = await LatestAttemptQuery(tenantId, orderId).FirstOrDefaultAsync(cancellationToken);
        if (attempt is null) return null;
        if (attempt.Status == PaymentAttemptStatus.Pending && clock.UtcNow > attempt.ExpiresAt)
        {
            attempt.Status = PaymentAttemptStatus.Expired;
            await db.SaveChangesAsync(cancellationToken);
        }
        return ToStatus(attempt);
    }

    public async Task<WompiPaymentStatusResult?> SynchronizeTransactionAsync(
        int tenantId,
        int orderId,
        string providerTransactionId,
        CancellationToken cancellationToken)
    {
        var attempt = await LatestAttemptQuery(tenantId, orderId).FirstOrDefaultAsync(cancellationToken);
        if (attempt is null) return null;
        var baseUrl = attempt.Environment == "production" ? "https://production.wompi.co" : "https://sandbox.wompi.co";
        using var response = await httpClient.GetAsync($"{baseUrl}/v1/transactions/{Uri.EscapeDataString(providerTransactionId)}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new BusinessException("No fue posible consultar la transacción en Wompi.");
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(raw);
        var transaction = document.RootElement.GetProperty("data");
        if (RequiredString(transaction, "reference") != attempt.Reference)
            throw new BusinessException("La transacción no pertenece a este intento de pago.");
        var amount = RequiredInt64(transaction, "amount_in_cents");
        var currency = RequiredString(transaction, "currency").ToUpperInvariant();
        if (amount != attempt.ExpectedAmountInCents || currency != attempt.Currency)
            throw new BusinessException("La transacción no coincide con el valor esperado.");
        var result = await ApplyTransactionObservationAsync(
            attempt,
            providerTransactionId,
            RequiredString(transaction, "status").ToUpperInvariant(),
            transaction.TryGetProperty("payment_method_type", out var method) ? method.GetString() : null,
            amount,
            currency,
            Sha256Hex(raw),
            clock.UtcNow,
            false,
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (result.RequiresManualReview)
            await NotifyReviewSafelyAsync(
                result.BranchId!.Value,
                result.OrderId!.Value,
                result.PaymentAttemptId!.Value,
                attempt.ManualReviewReason ?? "Pago que requiere revisión.",
                cancellationToken);
        return ToStatus(attempt);
    }

    private async Task NotifyReviewSafelyAsync(
        int branchId,
        int orderId,
        int paymentAttemptId,
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            await reviewNotifications.NotifyReviewRequiredAsync(branchId, orderId, paymentAttemptId, reason, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "No se pudo emitir la alerta de revisión del pago {PaymentAttemptId}.", paymentAttemptId);
        }
    }

    public async Task<WompiCheckoutData> RetryAsync(
        int tenantId,
        Order order,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (order.Status != OrderStatus.AwaitingPayment)
            throw new BusinessException("El pedido ya no está esperando pago.");
        var current = await LatestAttemptQuery(tenantId, order.Id).FirstOrDefaultAsync(cancellationToken);
        if (current is not null && current.Status == PaymentAttemptStatus.Pending && current.ExpiresAt > utcNow)
            return ToCheckout(current);
        var integration = await GetEnabledIntegrationAsync(tenantId, order.BranchId, cancellationToken)
            ?? throw new BusinessException("La sucursal no tiene pago en línea disponible.");
        return CreateAttempt(order, integration, utcNow);
    }

    public async Task<WompiManualReviewResult> ResolveManualReviewAsync(
        int attemptId,
        int reviewedByUserId,
        bool approve,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var attempt = await db.WompiPaymentAttempts
            .Include(x => x.Integration)
            .Include(x => x.Order)
            .FirstOrDefaultAsync(x => x.Id == attemptId, cancellationToken)
            ?? throw new BusinessException("Revisión de pago no encontrada.");
        if (!attempt.RequiresManualReview)
            throw new BusinessException("Este pago ya no requiere revisión.");
        if (approve)
        {
            if (attempt.Order.Status != OrderStatus.AwaitingPayment)
                throw new BusinessException("El pedido ya no puede activarse desde esta revisión.");
            ApproveAttempt(attempt, utcNow);
        }
        else
        {
            attempt.Status = attempt.AppPaymentId.HasValue
                ? PaymentAttemptStatus.Approved
                : PaymentAttemptStatus.Expired;
        }
        attempt.RequiresManualReview = false;
        attempt.ReviewedAt = utcNow;
        attempt.ReviewedByUserId = reviewedByUserId;
        await db.SaveChangesAsync(cancellationToken);
        return new(attempt.OrderId, attempt.Order.BranchId, attempt.Status.ToString(), attempt.Order.Status.ToString());
    }

    public async Task<bool> TestPublicKeyAsync(string environment, string publicKey, CancellationToken cancellationToken)
    {
        environment = NormalizeEnvironment(environment);
        var baseUrl = environment == "production" ? "https://production.wompi.co" : "https://sandbox.wompi.co";
        using var response = await httpClient.GetAsync($"{baseUrl}/v1/merchants/{Uri.EscapeDataString(publicKey)}", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private async Task<WompiWebhookProcessingResult> ApplyTransactionObservationAsync(
        WompiPaymentAttempt attempt,
        string providerTransactionId,
        string providerStatus,
        string? paymentMethod,
        long amountInCents,
        string currency,
        string payloadHash,
        DateTime observedAt,
        bool bypassExpiration,
        CancellationToken cancellationToken)
    {
        var providerTransaction = await db.WompiProviderTransactions
            .FirstOrDefaultAsync(x => x.ProviderTransactionId == providerTransactionId, cancellationToken);
        if (providerTransaction is not null && providerTransaction.PaymentAttemptId != attempt.Id)
            throw new BusinessException("La transacción Wompi ya está asociada a otro intento.");
        var alreadyAppliedApproval = providerStatus == "APPROVED"
            && providerTransaction?.Status == "APPROVED"
            && attempt.Status == PaymentAttemptStatus.Approved
            && attempt.AppPaymentId.HasValue;
        providerTransaction ??= new WompiProviderTransaction
        {
            PaymentAttemptId = attempt.Id,
            ProviderTransactionId = providerTransactionId,
        };
        if (providerTransaction.Id == 0) db.WompiProviderTransactions.Add(providerTransaction);
        providerTransaction.Status = providerStatus;
        providerTransaction.PaymentMethod = paymentMethod;
        providerTransaction.AmountInCents = amountInCents;
        providerTransaction.Currency = currency;
        providerTransaction.PayloadHash = payloadHash;
        providerTransaction.ObservedAt = observedAt;

        if (alreadyAppliedApproval)
            return new(true, false, false, attempt.Order.BranchId, attempt.OrderId, attempt.Id);

        if (providerStatus == "APPROVED")
        {
            var duplicateOrderPayment = attempt.Order.Status != OrderStatus.AwaitingPayment
                || await db.WompiPaymentAttempts.AnyAsync(x => x.OrderId == attempt.OrderId && x.AppPaymentId != null && x.Id != attempt.Id, cancellationToken);
            var late = !bypassExpiration && observedAt > attempt.ExpiresAt;
            if (duplicateOrderPayment || late)
            {
                attempt.Status = PaymentAttemptStatus.ReviewRequired;
                attempt.RequiresManualReview = true;
                attempt.ManualReviewReason = duplicateOrderPayment
                    ? "Wompi reportó un pago aprobado para un pedido que ya no esperaba pago. Verifica un posible cobro duplicado."
                    : "Wompi aprobó el pago después de vencer la ventana de 15 minutos. Valida que el pedido todavía pueda atenderse.";
            }
            else
            {
                ApproveAttempt(attempt, observedAt);
            }
        }
        else if (attempt.Status != PaymentAttemptStatus.Approved && !attempt.RequiresManualReview)
        {
            attempt.Status = providerStatus switch
            {
                "DECLINED" => PaymentAttemptStatus.Declined,
                "ERROR" => PaymentAttemptStatus.Error,
                "VOIDED" => PaymentAttemptStatus.Voided,
                _ => PaymentAttemptStatus.Pending,
            };
        }

        return new(true, false, attempt.RequiresManualReview, attempt.Order.BranchId, attempt.OrderId, attempt.Id);
    }

    private void ApproveAttempt(WompiPaymentAttempt attempt, DateTime approvedAt)
    {
        var amount = attempt.ExpectedAmountInCents / 100m;
        var commission = Math.Round(amount * attempt.Integration.EstimatedCommissionRate, 2, MidpointRounding.AwayFromZero);
        var appPayment = new AppPayment
        {
            OrderId = attempt.OrderId,
            AppId = attempt.Integration.FinancialAppId,
            Amount = amount,
            EstimatedCommissionRate = attempt.Integration.EstimatedCommissionRate,
            EstimatedCommissionAmount = commission,
            ExpectedNetAmount = amount - commission,
            IsSetted = false,
        };
        db.AppPayments.Add(appPayment);
        attempt.AppPayment = appPayment;
        attempt.Status = PaymentAttemptStatus.Approved;
        attempt.ApprovedAt = approvedAt;
        attempt.RequiresManualReview = false;
        attempt.ManualReviewReason = null;
        attempt.Order.Status = OrderStatus.Taken;
        attempt.Order.AddStatusTime(OrderStatus.Taken, approvedAt);
        db.PaymentNotificationOutboxMessages.Add(new PaymentNotificationOutboxMessage
        {
            TenantId = attempt.TenantId,
            BranchId = attempt.Order.BranchId,
            OrderId = attempt.OrderId,
            EventType = "order_payment_approved",
            Status = "pending",
            NextAttemptAt = approvedAt,
        });
    }

    private IQueryable<WompiPaymentAttempt> LatestAttemptQuery(int tenantId, int orderId) =>
        db.WompiPaymentAttempts
            .Include(x => x.Order)
            .Include(x => x.Integration)
            .Include(x => x.ProviderTransactions)
            .Where(x => x.TenantId == tenantId && x.OrderId == orderId)
            .OrderByDescending(x => x.Id);

    private WompiPaymentStatusResult ToStatus(WompiPaymentAttempt attempt) => new(
        attempt.OrderId,
        attempt.Order.Status.ToString(),
        attempt.Status.ToString(),
        attempt.RequiresManualReview,
        attempt.ManualReviewReason,
        attempt.ProviderTransactions.OrderByDescending(x => x.Id).Select(x => x.ProviderTransactionId).FirstOrDefault(),
        attempt.Status == PaymentAttemptStatus.Pending && attempt.ExpiresAt > clock.UtcNow ? ToCheckout(attempt) : null);

    private static WompiCheckoutData ToCheckout(WompiPaymentAttempt attempt) => new(
        attempt.PublicKeySnapshot,
        attempt.Currency,
        attempt.ExpectedAmountInCents,
        attempt.Reference,
        attempt.IntegritySignature,
        FormatExpiration(attempt.ExpiresAt),
        attempt.Environment);

    private string GetIntegritySecret(WompiPaymentIntegration integration, string environment) => secrets.Unprotect(
        environment == "production"
            ? integration.ProductionEncryptedIntegritySecret ?? throw new BusinessException("Falta el secreto de integridad de producción.")
            : integration.SandboxEncryptedIntegritySecret ?? throw new BusinessException("Falta el secreto de integridad de Sandbox."));

    private string GetEventsSecret(WompiPaymentIntegration integration, string environment) => secrets.Unprotect(
        GetEncryptedEventsSecret(integration, environment));

    private static string GetEncryptedEventsSecret(WompiPaymentIntegration integration, string environment) =>
        environment == "production"
            ? integration.ProductionEncryptedEventsSecret ?? throw new BusinessException("Falta el secreto de eventos de producción.")
            : integration.SandboxEncryptedEventsSecret ?? throw new BusinessException("Falta el secreto de eventos de Sandbox.");

    private static string GetPublicKey(WompiPaymentIntegration integration, string environment) =>
        environment == "production"
            ? integration.ProductionPublicKey ?? throw new BusinessException("Falta la llave pública de producción.")
            : integration.SandboxPublicKey ?? throw new BusinessException("Falta la llave pública de Sandbox.");

    internal static void ValidateCredentialPrefixes(string environment, string publicKey, string integritySecret, string eventsSecret)
    {
        var production = NormalizeEnvironment(environment) == "production";
        if (!publicKey.StartsWith(production ? "pub_prod_" : "pub_test_", StringComparison.Ordinal)
            || !integritySecret.StartsWith(production ? "prod_integrity_" : "test_integrity_", StringComparison.Ordinal)
            || !eventsSecret.StartsWith(production ? "prod_events_" : "test_events_", StringComparison.Ordinal))
            throw new BusinessException("Las credenciales no corresponden al ambiente seleccionado.");
    }

    private static bool ValidateEventChecksum(JsonElement root, string eventsSecret, string? receivedChecksum)
    {
        if (string.IsNullOrWhiteSpace(receivedChecksum)) return false;
        var data = root.GetProperty("data");
        var properties = root.GetProperty("signature").GetProperty("properties");
        var source = new StringBuilder();
        foreach (var property in properties.EnumerateArray())
        {
            var current = data;
            foreach (var segment in RequiredElementString(property).Split('.'))
                current = current.GetProperty(segment);
            source.Append(JsonScalar(current));
        }
        source.Append(root.GetProperty("timestamp").GetRawText());
        source.Append(eventsSecret);
        var expected = Encoding.UTF8.GetBytes(Sha256Hex(source.ToString()));
        var received = Encoding.UTF8.GetBytes(receivedChecksum.Trim().ToLowerInvariant());
        return expected.Length == received.Length && CryptographicOperations.FixedTimeEquals(expected, received);
    }

    private static string JsonScalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null => string.Empty,
        _ => value.GetRawText(),
    };

    private static string RequiredString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new JsonException($"Falta {property} en la transacción Wompi.");

    private static long RequiredInt64(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt64(out var result)
            ? result
            : throw new JsonException($"Falta {property} en la transacción Wompi.");

    private static string RequiredElementString(JsonElement element) =>
        element.GetString() ?? throw new JsonException("Propiedad de firma Wompi inválida.");

    private static string NormalizeEnvironment(string environment) => environment.Trim().ToLowerInvariant() switch
    {
        "sandbox" => "sandbox",
        "production" => "production",
        _ => throw new BusinessException("El ambiente Wompi debe ser Sandbox o Producción."),
    };

    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static string FormatExpiration(DateTime value) => NormalizeUtc(value).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    private static string Sha256Hex(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
