using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf.IO;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Helpers;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public sealed class InvoiceDeliveryTests
{
    internal static byte[] Logo => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEklEQVR4nGNkmJLFwMDAxAAGAAuoAQInK8WLAAAAAElFTkSuQmCC");
    internal static byte[] Xml(string number = "SETT1", string cufe = "abc", string kind = "invoice", int count = 1, string method = "47", string customerId = "12345")
    {
        XElement Party(string name, string id) => new("Party", new XElement("PartyTaxScheme", new XElement("RegistrationName", name), new XElement("CompanyID", id)),
            new XElement("PhysicalLocation", new XElement("Address", new XElement("AddressLine", new XElement("Line", "Calle 10 # 20-30")), new XElement("CityName", "Medellín"))),
            new XElement("Contact", new XElement("ElectronicMail", "facturas@example.com")));
        var credit = kind == "credit";
        var root = new XElement(credit ? "CreditNote" : "Invoice", new XElement("ID", number), new XElement("UUID", cufe),
            new XElement("IssueDate", "2026-10-22"), new XElement("IssueTime", "16:00:00-05:00"), new XElement("DocumentCurrencyCode", "COP"),
            new XElement("UBLExtensions", new XElement("QRCode", $"CUFE: {cufe}\nhttps://catalogo-vpfe.dian.gov.co/document/searchqr?documentkey={cufe}"),
                new XElement("InvoiceAuthorization", "18760000001"), new XElement("AuthorizationPeriod", new XElement("StartDate", "2025-01-01"), new XElement("EndDate", "2027-01-01")),
                new XElement("AuthorizedInvoices", new XElement("Prefix", "SETT"), new XElement("From", "1"), new XElement("To", "5000"))),
            new XElement("AccountingSupplierParty", Party("Restaurante de pruebas", "123")), new XElement("AccountingCustomerParty", Party("Cliente de pruebas", customerId)),
            new XElement("PaymentMeans", new XElement("ID", "1"), new XElement("PaymentMeansCode", method)),
            new XElement("LegalMonetaryTotal", new XElement("LineExtensionAmount", count * 10000), new XElement("PayableAmount", count * 10000)),
            Enumerable.Range(1, count).Select(index => new XElement(credit ? "CreditNoteLine" : "InvoiceLine",
                new XElement(credit ? "CreditedQuantity" : "InvoicedQuantity", 2), new XElement("LineExtensionAmount", 10000),
                new XElement("Price", new XElement("PriceAmount", 5000), new XElement("BaseQuantity", 1)),
                new XElement("Item", new XElement("Description", $"Producto {index}: arroz, proteína y acompañamientos seleccionados")))));
        return Encoding.UTF8.GetBytes(new XElement("AttachedDocument", new XElement("Attachment", new XElement("ExternalReference", new XElement("Description", new XCData(root.ToString()))))).ToString());
    }
    internal static ElectronicInvoiceDocument Document() => new()
    {
        Id = 1, TenantId = 1, Status = "issued", Prefix = "SETT", Number = 1, Cufe = "abc", Xml = Xml(),
        RecipientJson = """{"Name":"Cliente","Identification":"12345","Email":"client@example.com"}""",
        SnapshotJson = """{"issuer":{"TaxId":"123","BillingEmail":"billing@example.com"}}"""
    };
    [Fact]
    public void Restaurant_identity_and_logo_are_embedded_on_every_page()
    {
        var document = Document(); document.Xml = Xml(count: 40);
        var branding = new ElectronicInvoiceBranding { DisplayName = "Restaurante propio", Logo = Logo };
        InvoicePdf.ValidateLogo(branding.Logo);
        using var pdf = PdfReader.Open(new MemoryStream(InvoicePdf.Create(document, branding)), PdfDocumentOpenMode.Import);
        Assert.True(pdf.PageCount > 1);
        Assert.All(pdf.Pages.Cast<PdfSharp.Pdf.PdfPage>(), page => Assert.NotNull(page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/XObject")));
        Assert.Throws<BusinessException>(() => InvoicePdf.ValidateLogo(Encoding.UTF8.GetBytes("<svg/>")));
    }
    [Theory]
    [InlineData("invoice", "1", "Instrumento no definido")]
    [InlineData("invoice", "47", "Transferencia bancaria")]
    [InlineData("credit", "10", "Efectivo")]
    public void Copy_reads_signed_payment_and_identity_without_rewriting_history(string kind, string method, string label)
    {
        var document = Document(); document.Kind = kind; document.Xml = Xml(kind: kind, method: method);
        var data = InvoicePdf.Read(document);
        Assert.Equal(label, data.Method); Assert.Equal("Contado", data.Form);
        Assert.Equal(document.Cufe, data.Uuid); Assert.Contains(data.Uuid, data.Qr); Assert.Equal(10000, data.Total);
        Assert.Equal("Calle 10 # 20-30 · Medellín", data.IssuerAddress);
    }
    [Fact]
    public void Operational_payments_are_separate_from_the_XML_and_do_not_count_deposits_twice()
    {
        var document = Document(); document.Xml = Xml(method: "1");
        document.SnapshotJson = """{"order":{"PaidInStoreCashAmount":null,"bankPayments":[{"Amount":7000,"SourceReservationDepositId":null},{"Amount":3000,"SourceReservationDepositId":9}],"deposits":[{"Amount":3000,"IsEffective":false,"BankId":1}]}}""";
        var data = InvoicePdf.Read(document);
        Assert.Equal("Instrumento no definido", data.Method);
        Assert.Equal(10000, data.Payments.Sum(x => x.Amount));
        Assert.Contains(data.Payments, x => x.Label == "Transferencia bancaria" && x.Amount == 7000);
        Assert.Contains(data.Payments, x => x.Label == "Anticipo por transferencia" && x.Amount == 3000);
        Assert.NotEmpty(InvoicePdf.Create(document));
    }

    [Fact]
    public void Copy_rejects_cross_document_files_mismatched_totals_and_external_entities()
    {
        var document = Document(); document.Xml = Xml(cufe: "different");
        Assert.Throws<BusinessException>(() => InvoicePdf.Create(document));
        document.Xml = Xml(customerId: "99999");
        Assert.Throws<BusinessException>(() => InvoicePdf.Create(document));
        document.Xml = Xml(); document.PayloadJson = """{"totalToPay":9999}""";
        Assert.Throws<BusinessException>(() => InvoicePdf.Create(document));
        document.Xml = Encoding.UTF8.GetBytes("<!DOCTYPE Invoice [<!ENTITY secret SYSTEM 'file:///private'>]><Invoice>&secret;</Invoice>");
        Assert.Throws<XmlException>(() => InvoicePdf.Create(document));
    }
    [Fact]
    public void Portable_copy_paginates_and_can_render_the_existing_validated_document_without_sending()
    {
        var document = Document(); document.Xml = Xml(count: 65);
        var bytes = InvoicePdf.Create(document);
        using var pdf = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.True(pdf.PageCount > 2);
        Assert.All(pdf.Pages.Cast<PdfSharp.Pdf.PdfPage>(), page => Assert.True(page.Contents.Elements.Count > 0));
        var samplePath = Environment.GetEnvironmentVariable("INVOICE_PDF_SAMPLE");
        if (!string.IsNullOrWhiteSpace(samplePath))
        {
            Directory.CreateDirectory(samplePath);
            File.WriteAllBytes(Path.Combine(samplePath, "factura-prueba-multipagina.pdf"), bytes);
            var existingPath = Environment.GetEnvironmentVariable("INVOICE_PDF_SOURCE_XML");
            if (!string.IsNullOrWhiteSpace(existingPath))
            {
                document.Xml = File.ReadAllBytes(existingPath); document.Environment = "production";
                var envelope = XDocument.Load(existingPath);
                var embedded = XDocument.Parse(envelope.Descendants().First(x => x.Name.LocalName == "Attachment").Descendants().First(x => x.Name.LocalName == "Description").Value);
                var id = embedded.Root!.Elements().First(x => x.Name.LocalName == "ID").Value;
                var parts = System.Text.RegularExpressions.Regex.Match(id, @"^(.*?)(\d+)$");
                document.Prefix = parts.Groups[1].Value; document.Number = long.Parse(parts.Groups[2].Value);
                document.Cufe = embedded.Root.Elements().First(x => x.Name.LocalName == "UUID").Value;
                var snapshotPath = Environment.GetEnvironmentVariable("INVOICE_PDF_SOURCE_SNAPSHOT");
                document.SnapshotJson = string.IsNullOrWhiteSpace(snapshotPath) ? "{}" : File.ReadAllText(snapshotPath);
                document.RecipientJson = "{}";
                var logoPath = Environment.GetEnvironmentVariable("INVOICE_PDF_SOURCE_LOGO");
                var branding = string.IsNullOrWhiteSpace(logoPath) ? null : new ElectronicInvoiceBranding { DisplayName = Environment.GetEnvironmentVariable("INVOICE_PDF_DISPLAY_NAME") ?? "Restaurante", Logo = File.ReadAllBytes(logoPath) };
                File.WriteAllBytes(Path.Combine(samplePath, $"factura-{id}-copia.pdf"), InvoicePdf.Create(document, branding));
            }
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Own_email_attaches_exact_files_uses_verified_sender_and_stable_key(bool withLogo)
    {
        var tenant = new TestTenantContext();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, currentTenant: tenant, tenantExecutionContext: tenant);
        var document = Document(); document.Pdf = InvoicePdf.Create(document); document.PdfVersion = 1;
        if (withLogo) { db.ElectronicInvoiceBrandings.Add(new ElectronicInvoiceBranding { Id = 1, DisplayName = "Mi restaurante", Logo = Logo }); document.BrandingId = 1; }
        db.ElectronicInvoiceDocuments.Add(document); await db.SaveChangesAsync();
        var metadata = JsonSerializer.Serialize(new { fromName = "Restaurante <Pruebas>", replyTo = "billing@example.com", filename = "SETT1", pdfHash = InvoiceDeliveryService.Hash(document.Pdf), xmlHash = InvoiceDeliveryService.Hash(document.Xml!), logoHash = withLogo ? InvoiceDeliveryService.Hash(Logo) : null });
        var message = new EmailOutboxMessage { Id = 12, TenantId = 1, MessageType = "electronic_invoice", RelatedEntityId = 1, ToEmailsJson = "[\"client@example.com\"]", MetadataJson = metadata };
        var attempts = 0;
        var handler = new Handler(async request =>
        {
            attempts++;
            Assert.Equal("email-outbox-1-12", request.Headers.GetValues("Idempotency-Key").Single());
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Contains("verified@example.com", payload.RootElement.GetProperty("from").GetString());
            Assert.Equal("billing@example.com", payload.RootElement.GetProperty("reply_to").GetString());
            var attachments = payload.RootElement.GetProperty("attachments"); Assert.Equal(withLogo ? 3 : 2, attachments.GetArrayLength());
            if (withLogo) Assert.Equal("invoice-logo", attachments[2].GetProperty("content_id").GetString());
            Assert.Equal(document.Pdf, Convert.FromBase64String(attachments[0].GetProperty("content").GetString()!));
            Assert.Equal(document.Xml, Convert.FromBase64String(attachments[1].GetProperty("content").GetString()!));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"resend-123\"}") };
        });
        var sender = Sender(db, handler);
        Assert.Equal("resend-123", (await sender.SendAsync(message)).MessageId);
        using (tenant.BeginTenantScope(2)) Assert.False((await sender.SendAsync(message)).Success);
        document.Pdf = [1, 2, 3]; await db.SaveChangesAsync();
        Assert.True((await sender.SendAsync(message)).PermanentFailure); Assert.Equal(1, attempts);
    }
    internal static ResendEmailDeliveryService Sender(ApplicationDbContext db, HttpMessageHandler handler) => new(new HttpClient(handler),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ResendSettings:ApiKey"] = "test", ["ResendSettings:FromEmail"] = "verified@example.com" }).Build(),
        NullLogger<ResendEmailDeliveryService>.Instance, db);
    internal sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => callback(request);
    }
}
