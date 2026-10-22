using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Helpers;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Infrastructure.Integrations;

public sealed class ElectronicInvoiceService(ApplicationDbContext db, IEnumerable<IElectronicInvoiceProvider> providers, IClock clock, IConfiguration? configuration = null,
    IBackgroundWorkSignal<EmailOutboxWork>? emailSignal = null) : IElectronicInvoiceService
{
    private bool ProductionEnabled => configuration is not null && configuration.GetValue<bool>("ElectronicInvoicing:ProductionEnabled");
    public async Task<ElectronicInvoiceDocument> RequestAsync(Order order, BillingRecipientDto recipient, int userId, CancellationToken ct)
    {
        ValidateRecipient(recipient);
        if (order.Status == OrderStatus.Cancelled) throw new BusinessException("No se puede facturar un pedido cancelado.");
        var existing = await db.ElectronicInvoiceDocuments.FirstOrDefaultAsync(x => x.OrderId == order.Id && x.Kind == "invoice" && x.Status != "credited" && x.Status != "cancelled", ct);
        if (existing is not null) return existing;
        var integration = await db.ElectronicInvoiceIntegrations.SingleOrDefaultAsync(x => x.IsActive, ct)
            ?? throw new BusinessException("El negocio no tiene un proveedor de facturación activo.");
        if (integration.Environment == "production" && !ProductionEnabled)
            throw new BusinessException("La emisión real está desactivada en este servidor. Configura y valida primero el ambiente de pruebas.");
        var branding = await db.ElectronicInvoiceBrandings.OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct)
            ?? throw new BusinessException("Configura el nombre comercial y el logo del restaurante antes de solicitar facturas.");
        var document = new ElectronicInvoiceDocument
        {
            TenantId = order.TenantId, OrderId = order.Id, IntegrationId = integration.Id, BrandingId = branding.Id,
            Environment = integration.Environment, RequestedById = userId,
            RecipientJson = JsonSerializer.Serialize(recipient),
            EmailStatus = string.IsNullOrWhiteSpace(recipient.Email) ? "notRequested" : "pending",
            Status = (order.Type == OrderType.Reservation || order.ReservedFor.HasValue) && order.Status != OrderStatus.Delivered ? "scheduled" : "pending"
        };
        db.ElectronicInvoiceDocuments.Add(document);
        await db.SaveChangesAsync(ct);
        return document;
    }

    public static void ValidateRecipient(BillingRecipientDto recipient)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(recipient, new ValidationContext(recipient), errors, true)
            || string.IsNullOrWhiteSpace(recipient.Name) || string.IsNullOrWhiteSpace(recipient.Identification))
            throw new BusinessException("Completa identificación y nombre; verifica el correo de facturación.");
        if (recipient.DocumentType == 6) ColombianTaxId.Parse(recipient.Identification);
    }

    public static void EnsureReady(ElectronicInvoiceIntegration i)
    {
        if (string.IsNullOrWhiteSpace(i.EncryptedToken) || string.IsNullOrWhiteSpace(i.TaxId)
            || string.IsNullOrWhiteSpace(i.LegalName) || string.IsNullOrWhiteSpace(i.Resolution)
            || string.IsNullOrWhiteSpace(i.Prefix) || string.IsNullOrWhiteSpace(i.CreditPrefix)
            || i.TestedAt is null || i.ValidUntil is null || i.ValidUntil.Value.Date < DateTime.UtcNow.AddHours(-5).Date
            || i.NextNumber < 1 || i.NextNumber > i.LastNumber || i.NextCreditNumber < 1
            || i.Environment == "production" && !i.ProductionReady)
            throw new BusinessException("Completa y valida emisor, conexión, habilitación y numeración vigente antes de activar.");
    }

    public async Task ProcessAsync(int documentId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var claimed = await db.ElectronicInvoiceDocuments.Where(x => x.Id == documentId
            && (x.LeaseUntil == null || x.LeaseUntil < now)
            && (x.Status == "pending" || x.Status == "scheduled" || x.Status == "processing" || x.Status == "verifying" || x.Status == "issued" || x.Status == "credited"))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseUntil, now.AddMinutes(2)), ct);
        if (claimed != 1) return;
        var d = await db.ElectronicInvoiceDocuments.SingleAsync(x => x.Id == documentId, ct);
        await db.Entry(d).ReloadAsync(ct);
        var i = await db.ElectronicInvoiceIntegrations.SingleAsync(x => x.Id == d.IntegrationId, ct);
        if (i.Environment == "production" && d.SubmittedAt is null && !ProductionEnabled)
        {
            d.LeaseUntil = null; d.NextAttemptAt = now.AddMinutes(10);
            d.LastError = "Emisión real desactivada en el servidor. El documento no se ha transmitido.";
            await db.SaveChangesAsync(ct);
            return;
        }
        var provider = providers.Single(p => p.Code == i.Provider);
        var order = await db.Orders.Include(x => x.OrderDetails).ThenInclude(x => x.Product)
            .Include(x => x.BankPayments).Include(x => x.AppPayments).Include(x => x.Deposits).SingleAsync(x => x.Id == d.OrderId, ct);
        var numberPersisted = d.Number.HasValue;
        try
        {
            if (d.Status is "issued" or "credited")
            {
                if (d.Kind == "invoice" && order.Status == OrderStatus.Cancelled) d.CreditRequested = true;
                await CompleteFilesAndEmailAsync(d, i, provider, ct);
                d.LastError = null;
                return;
            }
            if (order.Status == OrderStatus.Cancelled && d.SubmittedAt is null && d.Kind == "invoice")
            {
                d.Status = "cancelled";
                return;
            }
            if (d.Status == "scheduled" && order.Status != OrderStatus.Delivered) return;
            if (d.Number is null)
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM public.\"order\" WHERE id = {order.Id} AND tenant_id = {order.TenantId} FOR UPDATE", ct);
                await db.Entry(order).ReloadAsync(ct);
                await db.Entry(order).Collection(x => x.OrderDetails).Query().Include(x => x.Product).LoadAsync(ct);
                if (d.Status == "scheduled" && order.Status != OrderStatus.Delivered) return;
                if (order.Status == OrderStatus.Cancelled && d.Kind == "invoice") { d.Status = "cancelled"; return; }
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({i.TenantId}, -782)", ct);
                await db.Entry(i).ReloadAsync(ct);
                if (d.Kind == "invoice") EnsureReady(i);
                else if (string.IsNullOrWhiteSpace(i.EncryptedToken) || string.IsNullOrWhiteSpace(i.CreditPrefix)
                    || i.Environment == "production" && !i.ProductionReady)
                    throw new BusinessException("Completa las credenciales y numeración de notas crédito.");
                d.Prefix = d.Kind == "credit" ? i.CreditPrefix : i.Prefix;
                var nextNumber = d.Kind == "credit" ? i.NextCreditNumber : i.NextNumber;
                if (await db.ElectronicInvoiceDocuments.AnyAsync(x => x.Environment == i.Environment && x.Kind == d.Kind
                    && x.Prefix == d.Prefix && x.Number == nextNumber, ct))
                    throw new BusinessException("El consecutivo está reservado por otro documento del negocio. Revisa la numeración antes de continuar.");
                d.Number = d.Kind == "credit" ? i.NextCreditNumber++ : i.NextNumber++;
                if (d.Kind == "invoice")
                {
                    d.SnapshotJson = BuildSnapshot(order, i);
                    var recipient = JsonSerializer.Deserialize<BillingRecipientDto>(d.RecipientJson)!;
                    d.PayloadJson = BuildInvoicePayload(order, recipient, i, d.Prefix, d.Number.Value, clock.UtcNow);
                }
                else
                {
                    var original = await db.ElectronicInvoiceDocuments.SingleAsync(x => x.Id == d.OriginalDocumentId, ct);
                    var payload = JsonNode.Parse(original.PayloadJson!)!.AsObject();
                    payload["prefix"] = d.Prefix; payload["number"] = d.Number.Value;
                    payload["invoiceReference"] = JsonSerializer.SerializeToNode(new { number = original.Prefix + original.Number, uuid = original.Cufe,
                        issue_date = JsonNode.Parse(original.PayloadJson!)!["date"]!.GetValue<string>() });
                    payload["discrepancy"] = JsonSerializer.SerializeToNode(new { code = 2, description = d.Reason });
                    SetDate(payload, clock.UtcNow);
                    d.PayloadJson = payload.ToJsonString();
                    d.SnapshotJson = original.SnapshotJson;
                }
                d.Status = "processing";
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                numberPersisted = true;
            }
            JsonElement? result;
            if (d.SubmittedAt.HasValue)
            {
                result = await provider.FindAsync(i, d.Kind, d.Prefix + d.Number, ct);
                if (result is null)
                {
                    var validationError = PlemsiInvoiceProvider.DataValidationError(d.ResponseJson);
                    d.Status = validationError is null ? "verifying" : "rejected";
                    d.LastError = validationError ?? "Sin confirmación del proveedor. Se consultará el mismo consecutivo; no se ha emitido otra factura.";
                    return;
                }
            }
            else
            {
                d.Status = "processing"; d.SubmittedAt = clock.UtcNow; d.AttemptCount++; d.ResponseJson = null;
                await db.SaveChangesAsync(ct);
                result = await provider.SubmitAsync(i, d.Kind, d.PayloadJson!, ct);
            }
            d.ResponseJson = result.Value.GetRawText();
            var valid = PlemsiInvoiceProvider.DianValidation(result.Value);
            d.Cufe = PlemsiInvoiceProvider.FindString(result.Value, "cufe", "cude", "uuid");
            d.Status = valid == true && !string.IsNullOrWhiteSpace(d.Cufe) ? "issued" : valid == false ? "rejected" : "verifying";
            d.LastError = d.Status == "issued" ? null : PlemsiInvoiceProvider.DianError(result.Value)
                ?? "Plemsi aún no confirma la validación DIAN. Se consultará el mismo consecutivo.";
            if (d.Status == "issued")
            {
                d.IssuedAt = clock.UtcNow;
                if (d.Kind == "credit")
                {
                    var original = await db.ElectronicInvoiceDocuments.SingleAsync(x => x.Id == d.OriginalDocumentId, ct);
                    original.Status = "credited";
                }
                if (order.Status == OrderStatus.Cancelled && d.Kind == "invoice") d.CreditRequested = true;
                await db.SaveChangesAsync(ct);
                await CompleteFilesAndEmailAsync(d, i, provider, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            if (!numberPersisted)
            {
                await db.Entry(d).ReloadAsync(CancellationToken.None);
                await db.Entry(i).ReloadAsync(CancellationToken.None);
            }
            if (d.Status is not ("issued" or "credited"))
                d.Status = ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.UnprocessableEntity }
                    ? "rejected" : d.SubmittedAt.HasValue ? "verifying" : ex is BusinessException ? "rejected" : "pending";
            d.LastError = ex is BusinessException ? ex.Message : d.Status is "issued" or "credited"
                ? "La factura sigue validada. No se pudo preparar su PDF/XML o correo; se reintentará sin volver a emitir."
                : "No se pudo confirmar la operación con Plemsi. Se conservará el mismo documento.";
            if (ex is PlemsiRequestException providerError)
            {
                try
                {
                    using var response = JsonDocument.Parse(providerError.ProviderResponse);
                    d.ResponseJson = response.RootElement.GetRawText();
                    var validationError = PlemsiInvoiceProvider.DataValidationError(d.ResponseJson);
                    if (validationError is not null && d.Status is not ("issued" or "credited")) d.Status = "rejected";
                    d.LastError = validationError ?? PlemsiInvoiceProvider.DianError(response.RootElement)
                        ?? PlemsiInvoiceProvider.FindString(response.RootElement, "StatusDescription", "message", "error") ?? d.LastError;
                }
                catch (JsonException) { }
            }
            d.AttemptCount++;
        }
        finally
        {
            d.LeaseUntil = null;
            d.NextAttemptAt = clock.UtcNow.AddSeconds(d.Status == "scheduled" ? 15 : Math.Min(600, 15 * Math.Pow(2, Math.Min(d.AttemptCount, 6))));
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task CompleteFilesAndEmailAsync(ElectronicInvoiceDocument d, ElectronicInvoiceIntegration i, IElectronicInvoiceProvider provider, CancellationToken ct)
    {
        d.Xml ??= await provider.DownloadAsync(i, d.Kind, d.Cufe!, "xml", ct);
        await db.SaveChangesAsync(ct);
        var delivery = new InvoiceDeliveryService(db, clock, configuration, emailSignal);
        await delivery.PreparePdfAsync(d.Id, ct);
        await delivery.QueueAsync(d.Id, false, ct);
    }

    public Task QueueEmailAsync(int documentId, CancellationToken ct) => new InvoiceDeliveryService(db, clock, configuration, emailSignal).QueueAsync(documentId, true, ct);
    public Task PreparePdfAsync(int documentId, CancellationToken ct) => new InvoiceDeliveryService(db, clock).PreparePdfAsync(documentId, ct);

    public static string BuildSnapshot(Order order, ElectronicInvoiceIntegration i) => JsonSerializer.Serialize(new
    {
        fiscalLines = FiscalInvoiceLines.Build(order).Select(x => new { name = x.Source.Product.Name, Quantity = x.Source.Quantity,
            UnitPrice = x.Net == 0 ? x.Source.UnitPrice : x.Net / x.Source.Quantity, Discount = x.Net == 0 ? (decimal)x.Source.UnitPrice * x.Source.Quantity : 0m, Net = x.Net }),
        discountPolicy = "net-prices-v1",
        issuer = new { i.TaxId, i.LegalName, i.BillingEmail, i.Resolution },
        order = new { order.Id, order.CustomerId, order.Total, order.Subtotal, order.DiscountTotal,
            order.DeliveryFee, order.PaidInStoreCashAmount,
            lines = order.OrderDetails.Select(x => new { x.ProductId, name = x.Product.Name, x.Quantity, x.UnitPrice, x.Discount }),
            bankPayments = order.BankPayments.Select(x => new { x.BankId, x.Amount, x.SourceReservationDepositId }),
            appPayments = order.AppPayments.Select(x => new { x.AppId, x.Amount }),
            deposits = order.Deposits.Select(x => new { x.Amount, x.IsEffective, x.BankId, x.AppId }) }
    });

    public static string BuildInvoicePayload(Order order, BillingRecipientDto recipient, ElectronicInvoiceIntegration integration, string prefix, long number, DateTime now)
    {
        ValidateRecipient(recipient);
        var items = new JsonArray();
        decimal total = 0;
        foreach (var fiscal in FiscalInvoiceLines.Build(order))
        {
            var line = fiscal.Source;
            decimal gross = (decimal)line.UnitPrice * line.Quantity;
            var net = fiscal.Net;
            var unit = net / line.Quantity;
            var exactUnit = decimal.Round(unit, 2) * line.Quantity == net;
            var fullyDiscounted = net == 0 && gross > 0;
            total += net;
            items.Add(JsonSerializer.SerializeToNode(new
            {
                unit_measure_id = 70, line_extension_amount = net, free_of_charge_indicator = net == 0,
                description = line.Product.Name, code = line.ProductId.ToString(), type_item_identification_id = 1,
                price_amount = fullyDiscounted ? line.UnitPrice : exactUnit ? unit : net, base_quantity = exactUnit || fullyDiscounted ? 1 : line.Quantity, invoiced_quantity = line.Quantity,
                allowance_charges = fullyDiscounted ? new[] { new { charge_indicator = false, allowance_charge_reason = fiscal.Gift ? "Obsequio" : "Descuento del pedido",
                    multiplier_factor_numeric = 100m, amount = gross, base_amount = gross } } : [],
                tax_totals = Array.Empty<object>()
            }));
        }
        var delivery = order.DeliveryFee ?? 0;
        if (delivery > 0)
        {
            items.Add(JsonSerializer.SerializeToNode(new { unit_measure_id = 70, line_extension_amount = delivery, free_of_charge_indicator = false,
                description = "Domicilio", code = "DELIVERY", type_item_identification_id = 1, price_amount = delivery, base_quantity = 1, invoiced_quantity = 1,
                tax_totals = Array.Empty<object>() }));
            total += delivery;
        }
        if (total != order.Total || total <= 0) throw new BusinessException("Los importes fiscales no coinciden con el total persistido del pedido.");
        var customer = JsonSerializer.SerializeToNode(new { identification_number = recipient.Identification, name = recipient.Name,
            email = recipient.Email, type_document_identification_id = recipient.DocumentType })!.AsObject();
        if (recipient.DocumentType == 6)
        {
            var nit = ColombianTaxId.Parse(recipient.Identification);
            customer["identification_number"] = nit.Number;
            customer["dv"] = nit.VerificationDigit;
        }
        var bankPaid = order.BankPayments.Where(x => x.SourceReservationDepositId is null).Sum(x => x.Amount)
            + order.Deposits.Where(x => !x.IsEffective && x.BankId.HasValue).Sum(x => x.Amount);
        var cashPaid = (order.PaidInStoreCashAmount ?? 0) + order.Deposits.Where(x => x.IsEffective).Sum(x => x.Amount);
        var appPaid = order.AppPayments.Sum(x => x.Amount) + order.Deposits.Where(x => !x.IsEffective && x.AppId.HasValue).Sum(x => x.Amount);
        var registeredPaid = (order.PaidInStoreCashAmount ?? 0) + order.BankPayments.Where(x => x.SourceReservationDepositId is null).Sum(x => x.Amount)
            + order.AppPayments.Sum(x => x.Amount) + order.Deposits.Sum(x => x.Amount);
        var payload = new JsonObject
        {
            ["prefix"] = prefix, ["number"] = number, ["resolution"] = integration.Resolution,
            ["orderReference"] = JsonSerializer.SerializeToNode(new { id_order = $"order-{order.TenantId}-{order.Id}" }),
            ["send_email"] = false,
            ["customer"] = customer,
            ["payment"] = JsonSerializer.SerializeToNode(new {
                payment_form_id = registeredPaid >= order.Total ? 1 : 2,
                payment_method_id = bankPaid > 0 && cashPaid == 0 && appPaid == 0 ? 47 : cashPaid > 0 && bankPaid == 0 && appPaid == 0 ? 10 : 1,
                payment_due_date = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)).ToOffset(TimeSpan.FromHours(-5)).ToString("yyyy-MM-dd"), duration_measure = "0" }),
            ["items"] = items, ["invoiceBaseTotal"] = total, ["allowanceTotal"] = 0,
            ["invoiceTaxExclusiveTotal"] = 0, ["invoiceTaxInclusiveTotal"] = total, ["totalToPay"] = total,
            ["allTaxTotals"] = new JsonArray(), ["allHoldingsTaxTotals"] = new JsonArray()
        };
        SetDate(payload, now);
        return payload.ToJsonString();
    }

    private static void SetDate(JsonObject payload, DateTime now)
    {
        var local = new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)).ToOffset(TimeSpan.FromHours(-5));
        payload["date"] = local.ToString("yyyy-MM-dd"); payload["time"] = local.ToString("HH:mm:ss");
    }
}
