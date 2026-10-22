using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Helpers;

namespace SenorArroz.Infrastructure.Services;

public sealed class InvoiceDeliveryService(ApplicationDbContext db, IClock clock, IConfiguration? configuration = null, IBackgroundWorkSignal<EmailOutboxWork>? workSignal = null)
{
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public async Task PreparePdfAsync(int documentId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(documentId, ct);
        var document = await db.ElectronicInvoiceDocuments.SingleAsync(x => x.Id == documentId, ct);
        await db.Entry(document).ReloadAsync(ct);
        if (document.PdfVersion < 1 || document.Pdf is null)
        {
            var pdf = InvoicePdf.Create(document);
            document.ProviderPdf ??= document.Pdf;
            document.Pdf = pdf;
            document.PdfVersion = 1;
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task QueueAsync(int documentId, bool resend, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(documentId, ct);
        var document = await db.ElectronicInvoiceDocuments.SingleAsync(x => x.Id == documentId, ct);
        await db.Entry(document).ReloadAsync(ct);
        if (document.Status is not ("issued" or "credited")) throw new BusinessException("Espera la validación DIAN antes de enviar el correo.");
        if (document.PdfVersion < 1 || document.Pdf is null || document.Xml is null)
            throw new BusinessException("Espera a que estén listos el PDF personalizado y el XML validado.");
        if (!resend && document.EmailStatus is "accepted" or "sent") return;
        var recipient = JsonSerializer.Deserialize<BillingRecipientDto>(document.RecipientJson)!;
        if (string.IsNullOrWhiteSpace(recipient.Email))
        {
            document.EmailStatus = "notRequested";
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return;
        }
        var previous = await db.EmailOutboxMessages.Where(x => x.RelatedEntityType == "electronic_invoice" && x.RelatedEntityId == documentId)
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (previous is not null)
        {
            if (!resend || previous.Status is "pending" or "processing" or "retry") return;
            if (previous.Status == "verifying") throw new BusinessException("El envío anterior requiere verificar su resultado antes de reenviar para evitar duplicados.");
            if (previous.SentAt > clock.UtcNow.AddMinutes(-1)) return;
        }
        var fiscal = InvoicePdf.Read(document);
        using var snapshot = JsonDocument.Parse(document.SnapshotJson);
        var replyTo = snapshot.RootElement.TryGetProperty("issuer", out var issuer) && issuer.TryGetProperty("BillingEmail", out var email) ? email.GetString() : null;
        var generation = await db.EmailOutboxMessages.CountAsync(x => x.RelatedEntityType == "electronic_invoice" && x.RelatedEntityId == documentId, ct);
        var label = document.Kind == "credit" ? "Nota crédito electrónica" : "Factura electrónica de venta";
        string H(string value) => WebUtility.HtmlEncode(value);
        var body = $"""
            <!doctype html><html lang="es"><body style="margin:0;background:#f5f8f7;font-family:Arial,sans-serif;color:#243b36;padding:24px">
            <main style="max-width:600px;margin:auto;background:white;border-radius:12px;border-top:4px solid #00946a;padding:28px">
            <p style="color:#00946a;font-weight:bold">{H(fiscal.Issuer)}</p><h1 style="font-size:23px">{H(label)} {H(fiscal.Number)}</h1>
            <p>Hola, {H(recipient.Name)}.</p><p>Adjuntamos la copia PDF y el XML del documento validado por la DIAN.</p>
            <p style="background:#edf8f3;padding:16px;font-size:20px"><strong>Total: {H(fiscal.Total.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("es-CO")))} {H(fiscal.Currency)}</strong></p>
            <p>Fecha de emisión: {H(fiscal.Date)}<br>Forma de pago: {H(fiscal.Form)}<br>Medio de pago: {H(fiscal.Method)}</p>
            <p style="font-size:12px;overflow-wrap:anywhere">{(fiscal.Credit ? "CUDE" : "CUFE")}: {H(fiscal.Uuid)}</p>
            <p style="color:#64756f;font-size:13px">El PDF incluye el código QR para consultar el documento en la DIAN. Conserva ambos archivos.</p>
            <p>Gracias por su compra.</p></main></body></html>
            """;
        db.EmailOutboxMessages.Add(new EmailOutboxMessage
        {
            TenantId = document.TenantId, MessageType = "electronic_invoice", RelatedEntityType = "electronic_invoice", RelatedEntityId = document.Id,
            DeduplicationKey = $"invoice-{document.Id}-delivery-{generation}", ToEmailsJson = JsonSerializer.Serialize(new[] { recipient.Email }),
            Subject = $"{label} {fiscal.Number} · {fiscal.Issuer}", Body = body, IsHtml = true, NextAttemptAt = clock.UtcNow,
            MetadataJson = JsonSerializer.Serialize(new { fromName = fiscal.Issuer, fromEmail = configuration?["ResendSettings:FromEmail"], replyTo, pdfHash = Hash(document.Pdf), xmlHash = Hash(document.Xml), filename = fiscal.Number })
        });
        document.EmailChannel = "application"; document.EmailStatus = "queued"; document.EmailError = null;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        workSignal?.Pulse();
    }

    private Task<int> LockAsync(int id, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT id FROM electronic_invoice_document WHERE id = {id} AND tenant_id = {db.CurrentTenantIdForFilter} FOR UPDATE", ct);
}
