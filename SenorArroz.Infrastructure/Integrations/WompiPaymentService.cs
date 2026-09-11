using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SenorArroz.Application.Common.Helpers;
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
    IWompiPaymentAttemptLock paymentAttemptLock,
    HttpClient httpClient,
    ILogger<WompiPaymentService> logger,
    IBackgroundWorkSignal<PaymentNotificationOutboxWork>? notificationSignal = null) : IWompiPaymentService
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

    public WompiCheckoutData CreateCheckoutAttempt(StorefrontCheckout checkout, WompiPaymentIntegration integration, DateTime utcNow)
    {
        if (checkout.Id <= 0)
            throw new BusinessException("El checkout debe existir antes de iniciar el pago.");
        if (checkout.Total <= 0)
            throw new BusinessException("El total del checkout no es válido para pago en línea.");
        if (checkout.OrderSource == "whatsapp_flow" && checkout.ExpiresAt <= utcNow)
            throw new BusinessException("El checkout de WhatsApp expiró. Inicia un pedido nuevo.");

        var environment = NormalizeEnvironment(integration.ActiveEnvironment);
        var publicKey = GetPublicKey(integration, environment);
        var integritySecret = GetIntegritySecret(integration, environment);
        ValidateCredentialPrefixes(environment, publicKey, integritySecret, GetEventsSecret(integration, environment));

        var expiresAt = checkout.OrderSource == "whatsapp_flow" ? NormalizeUtc(checkout.ExpiresAt) : NormalizeUtc(utcNow).AddMinutes(15);
        var reference = $"SA-{Guid.NewGuid():N}".ToUpperInvariant();
        var amountInCents = checked((long)checkout.Total * 100L);
        var expiration = FormatExpiration(expiresAt);
        var attempt = new WompiPaymentAttempt
        {
            TenantId = integration.TenantId,
            StorefrontCheckoutId = checkout.Id,
            IntegrationId = integration.Id,
            Reference = reference,
            Environment = environment,
            PublicKeySnapshot = publicKey,
            IntegritySignature = Sha256Hex($"{reference}{amountInCents}COP{expiration}{integritySecret}"),
            EncryptedEventsSecretSnapshot = GetEncryptedEventsSecret(integration, environment),
            ExpectedAmountInCents = amountInCents,
            Currency = "COP",
            Status = PaymentAttemptStatus.Pending,
            ExpiresAt = expiresAt,
        };
        checkout.ExpiresAt = expiresAt;
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
        var transaction = RequiredProperty(RequiredProperty(root, "data"), "transaction");
        var reference = RequiredString(transaction, "reference");
        var providerTransactionId = RequiredString(transaction, "id");
        var providerStatus = RequiredString(transaction, "status").ToUpperInvariant();
        var payloadHash = Sha256Hex(rawPayload);
        var timestamp = RequiredProperty(root, "timestamp").GetRawText();
        var fingerprint = Sha256Hex($"{environment}|{timestamp}|{providerTransactionId}|{providerStatus}|{payloadHash}");
        var amountInCents = RequiredInt64(transaction, "amount_in_cents");
        var currency = RequiredString(transaction, "currency").ToUpperInvariant();
        var processed = await paymentAttemptLock.ExecuteAsync(reference, async lockCancellationToken =>
        {
            var attempt = await PaymentAttemptByReferenceQuery(reference, environment)
                .FirstOrDefaultAsync(lockCancellationToken);
            if (attempt is null)
                return (Result: new WompiWebhookProcessingResult(false, false, false, null, null, null, "Referencia desconocida."), ReviewReason: (string?)null);

            var checksum = string.IsNullOrWhiteSpace(headerChecksum)
                ? RequiredString(RequiredProperty(root, "signature"), "checksum")
                : headerChecksum;
            if (!ValidateEventChecksum(root, secrets.Unprotect(attempt.EncryptedEventsSecretSnapshot), checksum))
                return (Result: new WompiWebhookProcessingResult(false, false, false, null, null, null, "Firma inválida."), ReviewReason: (string?)null);
            if (await db.WompiWebhookEvents.AnyAsync(x => x.EventFingerprint == fingerprint, lockCancellationToken))
                return (Result: new WompiWebhookProcessingResult(true, true, attempt.RequiresManualReview, AttemptBranchId(attempt), attempt.OrderId, attempt.Id), ReviewReason: attempt.ManualReviewReason);
            if (amountInCents != attempt.ExpectedAmountInCents || currency != attempt.Currency)
                return (Result: new WompiWebhookProcessingResult(false, false, false, null, null, null, "La transacción no coincide con el monto o moneda esperados."), ReviewReason: (string?)null);

            db.WompiWebhookEvents.Add(new WompiWebhookEvent
            {
                TenantId = attempt.TenantId,
                IntegrationId = attempt.IntegrationId,
                Environment = environment,
                EventFingerprint = fingerprint,
                PayloadHash = payloadHash,
                ProviderTransactionId = providerTransactionId,
                Status = "processed",
                ProcessedAt = clock.UtcNow,
            });
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
                lockCancellationToken);
            if (environment == "sandbox") attempt.Integration.LastSandboxWebhookAt = clock.UtcNow;
            else attempt.Integration.LastProductionWebhookAt = clock.UtcNow;
            await db.SaveChangesAsync(lockCancellationToken);
            notificationSignal?.Pulse();
            result = result with { BranchId = AttemptBranchId(attempt), OrderId = attempt.OrderId };
            return (Result: result, ReviewReason: attempt.ManualReviewReason);
        }, cancellationToken);

        if (processed.Result.RequiresManualReview && !processed.Result.Duplicate)
            await NotifyReviewSafelyAsync(
                processed.Result.BranchId!.Value,
                processed.Result.OrderId!.Value,
                processed.Result.PaymentAttemptId!.Value,
                processed.ReviewReason ?? "Pago que requiere revisión.",
                cancellationToken);
        return processed.Result;
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
        var attempt = await LatestAttemptQuery(tenantId, orderId).AsNoTracking().FirstOrDefaultAsync(cancellationToken);
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
        var observed = await paymentAttemptLock.ExecuteAsync(attempt.Reference, async lockCancellationToken =>
        {
            var current = await PaymentAttemptByIdQuery(attempt.Id).FirstAsync(lockCancellationToken);
            var result = await ApplyTransactionObservationAsync(
                current,
                providerTransactionId,
                RequiredString(transaction, "status").ToUpperInvariant(),
                transaction.TryGetProperty("payment_method_type", out var method) ? method.GetString() : null,
                amount,
                currency,
                Sha256Hex(raw),
                clock.UtcNow,
                false,
                lockCancellationToken);
            await db.SaveChangesAsync(lockCancellationToken);
            notificationSignal?.Pulse();
            return (Result: result, Status: ToStatus(current), ReviewReason: current.ManualReviewReason);
        }, cancellationToken);
        if (observed.Result.RequiresManualReview && !observed.Result.Duplicate)
            await NotifyReviewSafelyAsync(
                observed.Result.BranchId!.Value,
                observed.Result.OrderId!.Value,
                observed.Result.PaymentAttemptId!.Value,
                observed.ReviewReason ?? "Pago que requiere revisión.",
                cancellationToken);
        return observed.Status;
    }

    public async Task<WompiStorefrontCheckoutStatusResult?> GetCheckoutPaymentStatusAsync(
        int tenantId,
        string checkoutPublicId,
        CancellationToken cancellationToken)
    {
        var attempt = await LatestCheckoutAttemptQuery(tenantId, checkoutPublicId).FirstOrDefaultAsync(cancellationToken);
        if (attempt is null) return null;
        if (attempt.Status == PaymentAttemptStatus.Pending && clock.UtcNow > attempt.ExpiresAt)
        {
            attempt.Status = PaymentAttemptStatus.Expired;
            attempt.StorefrontCheckout!.Status = "expired";
            await db.SaveChangesAsync(cancellationToken);
        }
        return ToCheckoutStatus(attempt);
    }

    public async Task<WompiStorefrontCheckoutStatusResult?> SynchronizeCheckoutTransactionAsync(
        int tenantId,
        string checkoutPublicId,
        string providerTransactionId,
        CancellationToken cancellationToken)
    {
        var attempt = await LatestCheckoutAttemptQuery(tenantId, checkoutPublicId).AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (attempt is null) return null;
        var baseUrl = attempt.Environment == "production" ? "https://production.wompi.co" : "https://sandbox.wompi.co";
        using var response = await httpClient.GetAsync($"{baseUrl}/v1/transactions/{Uri.EscapeDataString(providerTransactionId)}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new BusinessException("No fue posible consultar la transacción en Wompi.");
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(raw);
        var transaction = document.RootElement.GetProperty("data");
        if (RequiredString(transaction, "reference") != attempt.Reference)
            throw new BusinessException("La transacción no pertenece a este checkout.");
        var amount = RequiredInt64(transaction, "amount_in_cents");
        var currency = RequiredString(transaction, "currency").ToUpperInvariant();
        if (amount != attempt.ExpectedAmountInCents || currency != attempt.Currency)
            throw new BusinessException("La transacción no coincide con el valor esperado.");
        var observed = await paymentAttemptLock.ExecuteAsync(attempt.Reference, async lockCancellationToken =>
        {
            var current = await PaymentAttemptByIdQuery(attempt.Id).FirstAsync(lockCancellationToken);
            var result = await ApplyTransactionObservationAsync(
                current,
                providerTransactionId,
                RequiredString(transaction, "status").ToUpperInvariant(),
                transaction.TryGetProperty("payment_method_type", out var method) ? method.GetString() : null,
                amount,
                currency,
                Sha256Hex(raw),
                clock.UtcNow,
                false,
                lockCancellationToken);
            await db.SaveChangesAsync(lockCancellationToken);
            notificationSignal?.Pulse();
            return (Result: result, Status: ToCheckoutStatus(current), ReviewReason: current.ManualReviewReason);
        }, cancellationToken);
        if (observed.Result.RequiresManualReview && !observed.Result.Duplicate && observed.Result.OrderId.HasValue)
            await NotifyReviewSafelyAsync(
                observed.Result.BranchId!.Value,
                observed.Result.OrderId.Value,
                observed.Result.PaymentAttemptId!.Value,
                observed.ReviewReason ?? "Pago que requiere revisión.",
                cancellationToken);
        return observed.Status;
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

    public async Task<WompiCheckoutData> RetryCheckoutAsync(
        int tenantId,
        StorefrontCheckout checkout,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (checkout.OrderId.HasValue || checkout.Status == "approved")
            throw new BusinessException("Este checkout ya generó un pedido.");
        var current = await LatestCheckoutAttemptQuery(tenantId, checkout.PublicId).FirstOrDefaultAsync(cancellationToken);
        if (current is not null && current.Status == PaymentAttemptStatus.Pending && current.ExpiresAt > utcNow)
            return ToCheckout(current);
        var integration = await GetEnabledIntegrationAsync(tenantId, checkout.BranchId, cancellationToken)
            ?? throw new BusinessException("La sucursal no tiene pago en línea disponible.");
        checkout.Status = "pending";
        return CreateCheckoutAttempt(checkout, integration, utcNow);
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
            if (attempt.Order?.Status != OrderStatus.AwaitingPayment)
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
        notificationSignal?.Pulse();
        return new(attempt.OrderId!.Value, attempt.Order!.BranchId, attempt.Status.ToString(), attempt.Order.Status.ToString());
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
        var repeatedObservation = providerTransaction?.Status == providerStatus;
        var alreadyAppliedApproval = providerStatus == "APPROVED"
            && repeatedObservation
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
            return new(true, true, false, AttemptBranchId(attempt), attempt.OrderId, attempt.Id);

        if (providerStatus == "APPROVED")
        {
            if (attempt.StorefrontCheckout is not null && attempt.Order is null)
                attempt.Order = await CreateOrderFromCheckoutAsync(attempt.StorefrontCheckout, observedAt, cancellationToken);

            var duplicateOrderPayment = attempt.Order is null
                || attempt.Order.Status != OrderStatus.AwaitingPayment
                || await db.WompiPaymentAttempts.AnyAsync(x => x.OrderId != null && x.OrderId == attempt.OrderId && x.AppPaymentId != null && x.Id != attempt.Id, cancellationToken);
            var late = !bypassExpiration && observedAt > attempt.ExpiresAt;
            if (duplicateOrderPayment || late)
            {
                attempt.Status = PaymentAttemptStatus.ReviewRequired;
                attempt.RequiresManualReview = true;
                if (attempt.StorefrontCheckout is not null)
                    attempt.StorefrontCheckout.Status = "review_required";
                attempt.ManualReviewReason = duplicateOrderPayment
                    ? "Wompi reportó un pago aprobado para un pedido que ya no esperaba pago. Verifica un posible cobro duplicado."
                    : "Wompi aprobó el pago después de vencer la ventana de 15 minutos. Valida que el pedido todavía pueda atenderse.";
                if (attempt.StorefrontCheckout is not null)
                    await EnqueueWhatsAppCheckoutMessageAsync(attempt.StorefrontCheckout, "payment-review-required",
                        "Wompi reportó tu pago, pero requiere revisión antes de confirmar el pedido. Un asesor debe verificarlo; no vuelvas a pagar.", observedAt, cancellationToken);
            }
            else
            {
                ApproveAttempt(attempt, observedAt);
                if (attempt.StorefrontCheckout is not null)
                    await EnqueueWhatsAppCheckoutMessageAsync(attempt.StorefrontCheckout, "payment-approved",
                        "Tu pago fue aprobado y el pedido quedó confirmado. La sede asignada continuará contigo por este chat.", observedAt, cancellationToken);
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
            if (attempt.StorefrontCheckout is not null)
            {
                attempt.StorefrontCheckout.Status = attempt.Status switch
                {
                    PaymentAttemptStatus.Declined => "declined",
                    PaymentAttemptStatus.Error => "error",
                    PaymentAttemptStatus.Voided => "voided",
                    _ => "pending",
                };
                if (attempt.Status is PaymentAttemptStatus.Declined or PaymentAttemptStatus.Error or PaymentAttemptStatus.Voided)
                    await EnqueueWhatsAppCheckoutMessageAsync(attempt.StorefrontCheckout, $"payment-{attempt.Status.ToString().ToLowerInvariant()}",
                        "No pudimos completar el pago. Escribe “reintentar pago” para recibir un enlace vigente o pide ayuda a un asesor.", observedAt, cancellationToken);
            }
        }

        return new(true, repeatedObservation, attempt.RequiresManualReview, AttemptBranchId(attempt), attempt.OrderId, attempt.Id);
    }

    private void ApproveAttempt(WompiPaymentAttempt attempt, DateTime approvedAt)
    {
        var order = attempt.Order ?? throw new BusinessException("No existe un pedido para aplicar el pago aprobado.");
        var amount = attempt.ExpectedAmountInCents / 100m;
        var commission = Math.Round(amount * attempt.Integration.EstimatedCommissionRate, 2, MidpointRounding.AwayFromZero);
        var appPayment = new AppPayment
        {
            Order = order,
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
        order.Status = OrderStatus.Taken;
        order.AddStatusTime(OrderStatus.Taken, approvedAt);
        if (attempt.StorefrontCheckout is not null)
        {
            attempt.StorefrontCheckout.Status = "approved";
            attempt.StorefrontCheckout.Order = order;
        }
        db.PaymentNotificationOutboxMessages.Add(new PaymentNotificationOutboxMessage
        {
            TenantId = attempt.TenantId,
            BranchId = order.BranchId,
            Order = order,
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

    private IQueryable<WompiPaymentAttempt> PaymentAttemptByReferenceQuery(string reference, string environment) =>
        PaymentAttemptQuery().Where(x => x.Reference == reference && x.Environment == environment);

    private IQueryable<WompiPaymentAttempt> PaymentAttemptByIdQuery(int attemptId) =>
        PaymentAttemptQuery().Where(x => x.Id == attemptId);

    private IQueryable<WompiPaymentAttempt> PaymentAttemptQuery() =>
        db.WompiPaymentAttempts
            .Include(x => x.Integration)
            .Include(x => x.Order)
            .Include(x => x.StorefrontCheckout).ThenInclude(x => x!.SavedAddress)
            .Include(x => x.ProviderTransactions);

    private IQueryable<WompiPaymentAttempt> LatestCheckoutAttemptQuery(int tenantId, string checkoutPublicId) =>
        db.WompiPaymentAttempts
            .Include(x => x.Order)
            .Include(x => x.Integration)
            .Include(x => x.StorefrontCheckout).ThenInclude(x => x!.SavedAddress)
            .Include(x => x.ProviderTransactions)
            .Where(x => x.TenantId == tenantId && x.StorefrontCheckout != null && x.StorefrontCheckout.PublicId == checkoutPublicId)
            .OrderByDescending(x => x.Id);

    private WompiPaymentStatusResult ToStatus(WompiPaymentAttempt attempt) => new(
        attempt.OrderId!.Value,
        attempt.Order!.Status.ToString(),
        attempt.Status.ToString(),
        attempt.RequiresManualReview,
        attempt.ManualReviewReason,
        attempt.ProviderTransactions.OrderByDescending(x => x.Id).Select(x => x.ProviderTransactionId).FirstOrDefault(),
        attempt.Status == PaymentAttemptStatus.Pending && attempt.ExpiresAt > clock.UtcNow ? ToCheckout(attempt) : null);

    private WompiStorefrontCheckoutStatusResult ToCheckoutStatus(WompiPaymentAttempt attempt) => new(
        attempt.StorefrontCheckout!.PublicId,
        attempt.OrderId,
        attempt.StorefrontCheckout.Status,
        attempt.Status.ToString(),
        attempt.RequiresManualReview,
        attempt.ManualReviewReason,
        attempt.ProviderTransactions.OrderByDescending(x => x.Id).Select(x => x.ProviderTransactionId).FirstOrDefault(),
        attempt.Status == PaymentAttemptStatus.Pending && attempt.ExpiresAt > clock.UtcNow ? ToCheckout(attempt) : null);

    private static int AttemptBranchId(WompiPaymentAttempt attempt) =>
        attempt.Order?.BranchId ?? attempt.StorefrontCheckout?.BranchId
        ?? throw new BusinessException("El intento no está asociado a un pedido ni checkout.");

    private async Task<Order> CreateOrderFromCheckoutAsync(
        StorefrontCheckout checkout,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        if (checkout.Order is not null) return checkout.Order;

        var branch = await db.Branches.FirstOrDefaultAsync(x => x.Id == checkout.BranchId && x.IsActive, cancellationToken)
            ?? throw new BusinessException("La sucursal del checkout ya no está disponible.");
        if (!branch.StorefrontTakenByUserId.HasValue)
            throw new BusinessException("La sucursal no tiene usuario técnico para pedidos web.");

        Customer customer;
        if (checkout.CustomerId.HasValue)
        {
            customer = await db.Customers.FirstOrDefaultAsync(x => x.Id == checkout.CustomerId && x.Active, cancellationToken)
                ?? throw new BusinessException("El cliente verificado ya no está disponible.");
        }
        else
        {
            customer = await db.Customers.FirstOrDefaultAsync(x => x.Active && (x.Phone1 == checkout.CustomerPhone || x.Phone2 == checkout.CustomerPhone), cancellationToken)
                ?? new Customer
                {
                    BranchId = checkout.BranchId,
                    Name = checkout.CustomerName,
                    Phone1 = checkout.CustomerPhone,
                    Active = true,
                };
            if (customer.Id == 0) db.Customers.Add(customer);
        }

        Address? address = checkout.SavedAddress;
        if (checkout.FulfillmentType == "delivery" && address is null)
        {
            var hasAddresses = customer.Id != 0 && await db.Addresses.AnyAsync(x => x.CustomerId == customer.Id, cancellationToken);
            address = new Address
            {
                Customer = customer,
                Label = string.IsNullOrWhiteSpace(checkout.AddressLabel) ? "Casa" : checkout.AddressLabel,
                AddressText = checkout.FormattedAddress!,
                AdditionalInfo = checkout.AddressAdditionalInfo,
                DeliveryFee = checkout.DeliveryFee,
                Latitude = checkout.Latitude,
                Longitude = checkout.Longitude,
                IsPrimary = !hasAddresses,
                OriginalAddressText = checkout.OriginalAddress,
                NormalizedAddressText = checkout.FormattedAddress,
                ValidationSource = "storefront_google",
                ValidatedAt = observedAt,
            };
            db.Addresses.Add(address);
        }

        var order = new Order
        {
            BranchId = checkout.BranchId,
            TakenById = branch.StorefrontTakenByUserId.Value,
            Customer = customer,
            Address = address,
            GuestName = checkout.CustomerName,
            Type = checkout.FulfillmentType == "delivery" ? OrderType.Delivery : OrderType.Onsite,
            DeliveryFee = checkout.DeliveryFee,
            Status = OrderStatus.AwaitingPayment,
            OrderSource = checkout.OrderSource,
            WhatsAppConversationId = checkout.WhatsAppConversationId,
            StorefrontIdempotencyKey = checkout.IdempotencyKey,
            Notes = checkout.OrderNotes,
            AppliedBenefitType = checkout.AppliedBenefitType,
            AppliedBenefitSourceId = checkout.AppliedBenefitSourceId,
            AppliedBenefitLabel = checkout.AppliedBenefitLabel,
            AppliedBenefitRewardType = checkout.AppliedBenefitRewardType,
            AppliedBenefitAmount = checkout.AppliedBenefitAmount,
            AppliedBenefitSnapshot = checkout.AppliedBenefitSnapshot,
            FreeDeliveryRequested = checkout.AppliedBenefitRewardType == LoyaltyRewardType.FreeDelivery,
        };
        order.AddStatusTime(OrderStatus.AwaitingPayment, observedAt);
        var lines = JsonSerializer.Deserialize<List<StorefrontCheckoutLine>>(checkout.ItemsJson) ?? [];
        foreach (var line in lines)
            order.OrderDetails.Add(new OrderDetail
            {
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Discount = line.Discount,
                Subtotal = line.Subtotal,
                Notes = line.Notes,
            });
        OrderTotalsHelper.RecalculateFromOrderDetails(order);
        if (order.Total != checkout.Total)
            throw new BusinessException("El total persistido del checkout ya no coincide.");

        checkout.Order = order;
        db.Orders.Add(order);
        return order;
    }

    private async Task EnqueueWhatsAppCheckoutMessageAsync(
        StorefrontCheckout checkout,
        string eventType,
        string body,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        if (!checkout.WhatsAppConversationId.HasValue) return;
        var eventKey = $"whatsapp-{eventType}:{checkout.PublicId}";
        if (await db.WhatsAppCommerceOutboxMessages.AnyAsync(x => x.EventKey == eventKey, cancellationToken)) return;
        var conversation = await db.WhatsAppConversations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == checkout.WhatsAppConversationId.Value && x.ChannelSettingId != null, cancellationToken);
        if (conversation?.ChannelSettingId is not int channelSettingId) return;
        var session = await db.WhatsAppCommerceSessions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.IdempotencyKey == checkout.IdempotencyKey && x.ConversationId == conversation.Id, cancellationToken);
        if (session is not null)
        {
            db.WhatsAppCommerceEvents.Add(new WhatsAppCommerceEvent
            {
                TenantId = checkout.TenantId,
                SessionId = session.Id,
                ConversationId = conversation.Id,
                BranchId = checkout.BranchId,
                EventKey = $"{session.CorrelationId:N}:{eventType}",
                EventName = eventType.Replace('-', '_'),
                Screen = "COMPLETE",
                ReferenceId = checkout.OrderId?.ToString() ?? checkout.PublicId
            });
            if (eventType == "payment-approved" && checkout.OrderId.HasValue)
                db.WhatsAppCommerceEvents.Add(new WhatsAppCommerceEvent
                {
                    TenantId = checkout.TenantId, SessionId = session.Id, ConversationId = conversation.Id,
                    BranchId = checkout.BranchId, EventKey = $"{session.CorrelationId:N}:order-created",
                    EventName = "order_created", Screen = "COMPLETE", ReferenceId = checkout.OrderId.Value.ToString()
                });
        }
        db.WhatsAppCommerceOutboxMessages.Add(new WhatsAppCommerceOutboxMessage
        {
            TenantId = checkout.TenantId,
            ChannelSettingId = channelSettingId,
            ConversationId = conversation.Id,
            EventKey = eventKey,
            Body = body,
            NextAttemptAt = observedAt
        });
    }

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
        var data = RequiredProperty(root, "data");
        var properties = RequiredProperty(RequiredProperty(root, "signature"), "properties");
        if (properties.ValueKind != JsonValueKind.Array)
            throw new JsonException("Las propiedades de firma Wompi no son válidas.");
        var source = new StringBuilder();
        foreach (var property in properties.EnumerateArray())
        {
            var current = data;
            foreach (var segment in RequiredElementString(property).Split('.'))
                current = RequiredProperty(current, segment);
            source.Append(JsonScalar(current));
        }
        source.Append(RequiredProperty(root, "timestamp").GetRawText());
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
        element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new JsonException($"Falta {property} en la transacción Wompi.");

    private static long RequiredInt64(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(property, out var value)
            && value.TryGetInt64(out var result)
            ? result
            : throw new JsonException($"Falta {property} en la transacción Wompi.");

    private static string RequiredElementString(JsonElement element) =>
        element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()!
            : throw new JsonException("Propiedad de firma Wompi inválida.");

    private static JsonElement RequiredProperty(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            ? value
            : throw new JsonException($"Falta {property} en el webhook Wompi.");

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
