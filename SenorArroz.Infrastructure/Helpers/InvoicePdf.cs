using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using QRCoder;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Helpers;

public static class InvoicePdf
{
    public const int CurrentVersion = 2;
    public static void ValidateLogo(byte[] bytes)
    {
        if (bytes.Length is 0 or > 500000 || !(bytes.AsSpan().StartsWith(new byte[] {137,80,78,71,13,10,26,10}) || bytes.AsSpan().StartsWith(new byte[] {255,216,255})))
            throw new BusinessException("El logo debe ser PNG o JPG de máximo 500 KB.");
        try
        {
            using var image = XImage.FromStream(new MemoryStream(bytes));
            if (image.PixelWidth > 3000 || image.PixelHeight > 3000) throw new BusinessException("El logo admite hasta 3000 × 3000 píxeles.");
        }
        catch (BusinessException) { throw; }
        catch (Exception) { throw new BusinessException("No se pudo leer el logo. Selecciona una imagen PNG o JPG válida."); }
    }
    private static readonly CultureInfo Colombian = CultureInfo.GetCultureInfo("es-CO");
    private static readonly object FontLock = new();
    private static readonly XBrush Ink = new XSolidBrush(XColor.FromArgb(28, 43, 55));
    private static readonly XBrush Muted = new XSolidBrush(XColor.FromArgb(92, 109, 120));
    private static readonly XBrush Green = new XSolidBrush(XColor.FromArgb(0, 148, 106));
    public sealed record FiscalLine(string Name, decimal Quantity, decimal UnitPrice, decimal Discount, decimal Net);
    public sealed record Data(string Number, string Uuid, string Qr, string Date, string Time, string Currency,
        string Issuer, string TradeName, string IssuerId, string IssuerAddress, string IssuerContact,
        string Customer, string CustomerId, string CustomerContact, string Form, string Method, string Due,
        string Resolution, string Authorization, string Reference, decimal Net, decimal Taxes, decimal Discount,
        decimal Charges, decimal Prepaid, decimal Rounding, decimal Total, IReadOnlyList<FiscalLine> Lines,
        IReadOnlyList<(string Label, decimal Amount)> Payments, bool Credit);

    private static XDocument Load(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8_000_000
        });
        return XDocument.Load(reader);
    }
    private static XElement? Child(XElement? e, string name) => e?.Elements().FirstOrDefault(x => x.Name.LocalName == name);
    private static string Value(XElement? e, string name) => Child(e, name)?.Value.Trim() ?? "";
    private static string Deep(XElement? e, string name) => e?.Descendants().FirstOrDefault(x => x.Name.LocalName == name)?.Value.Trim() ?? "";
    private static decimal Amount(XElement? e, string name) => decimal.TryParse(Value(e, name), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : 0;
    private static string PartyId(XElement? party)
    {
        var id = Child(Child(party, "PartyTaxScheme"), "CompanyID");
        return id is null ? "" : id.Value + (id.Attribute("schemeID") is { } dv ? "-" + dv.Value : "");
    }
    public static Data Read(ElectronicInvoiceDocument document)
    {
        if (document.Status is not ("issued" or "credited") || document.Xml is null || string.IsNullOrWhiteSpace(document.Cufe))
            throw new BusinessException("Espera el XML y la validación DIAN antes de preparar la copia.");
        var root = Load(Encoding.UTF8.GetString(document.Xml)).Root!;
        if (root.Name.LocalName == "AttachedDocument")
        {
            var embedded = Child(Child(Child(root, "Attachment"), "ExternalReference"), "Description")?.Value;
            if (string.IsNullOrWhiteSpace(embedded)) throw new BusinessException("El XML no contiene el documento fiscal validado.");
            root = Load(embedded).Root!;
        }
        var credit = document.Kind == "credit";
        var uuid = Value(root, "UUID");
        if (root.Name.LocalName != (credit ? "CreditNote" : "Invoice") || Value(root, "ID") != document.Prefix + document.Number
            || !string.Equals(uuid, document.Cufe, StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("El XML no corresponde al consecutivo y CUFE/CUDE de este documento.");
        var qr = Deep(root, "QRCode");
        if (string.IsNullOrWhiteSpace(qr) || !qr.Contains(uuid, StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("El XML validado no contiene un QR asociado al CUFE/CUDE.");
        var supplier = Child(Child(root, "AccountingSupplierParty"), "Party");
        var customer = Child(Child(root, "AccountingCustomerParty"), "Party");
        var recipient = JsonSerializer.Deserialize<BillingRecipientDto>(document.RecipientJson)!;
        var customerId = Child(Child(customer, "PartyTaxScheme"), "CompanyID")?.Value.Trim() ?? "";
        var expectedId = recipient.DocumentType == 6 ? recipient.Identification.Split('-')[0] : recipient.Identification;
        if (!string.IsNullOrWhiteSpace(expectedId) && expectedId != customerId)
            throw new BusinessException("El comprador del XML no coincide con el destinatario fiscal del documento.");
        var environment = Value(root, "ProfileExecutionID");
        if (environment.Length > 0 && environment != (document.Environment == "production" ? "1" : "2"))
            throw new BusinessException("El ambiente del XML no coincide con el documento fiscal.");
        var money = Child(root, "LegalMonetaryTotal");
        var payment = Child(root, "PaymentMeans");
        var period = root.Descendants().FirstOrDefault(x => x.Name.LocalName == "AuthorizationPeriod");
        var range = root.Descendants().FirstOrDefault(x => x.Name.LocalName == "AuthorizedInvoices");
        var issuer = Deep(Child(supplier, "PartyTaxScheme"), "RegistrationName");
        var tradeName = Value(Child(supplier, "PartyName"), "Name");
        var issuerId = PartyId(supplier);
        var total = Amount(money, "PayableAmount");
        using var snapshot = JsonDocument.Parse(document.SnapshotJson);
        if (snapshot.RootElement.TryGetProperty("issuer", out var expectedIssuer)
            && expectedIssuer.TryGetProperty("TaxId", out var taxId) && taxId.GetString() is { Length: > 0 } expectedTaxId
            && issuerId.Split('-')[0] != expectedTaxId.Split('-')[0])
            throw new BusinessException("El emisor del XML no coincide con el snapshot fiscal del negocio.");
        if (document.PayloadJson is { } payload)
        {
            using var expected = JsonDocument.Parse(payload);
            if (expected.RootElement.TryGetProperty("totalToPay", out var payable) && payable.GetDecimal() != total)
                throw new BusinessException("El total del XML no coincide con el documento transmitido.");
        }
        var lines = root.Elements().Where(x => x.Name.LocalName == (credit ? "CreditNoteLine" : "InvoiceLine")).Select(x =>
        {
            var price = Child(x, "Price");
            var basis = Amount(price, "BaseQuantity");
            var discount = x.Elements().Where(a => a.Name.LocalName == "AllowanceCharge" && Value(a, "ChargeIndicator") == "false").Sum(a => Amount(a, "Amount"));
            return new FiscalLine(Deep(Child(x, "Item"), "Description"), Amount(x, credit ? "CreditedQuantity" : "InvoicedQuantity"),
                Amount(price, "PriceAmount") / (basis == 0 ? 1 : basis), discount, Amount(x, "LineExtensionAmount"));
        }).ToArray();
        if (lines.Length == 0 || lines.Sum(x => x.Net) != Amount(money, "LineExtensionAmount"))
            throw new BusinessException("Las líneas del XML no coinciden con su total fiscal.");
        var form = Value(payment, "ID") == "1" ? "Contado" : Value(payment, "ID") == "2" ? "Crédito" : "No informada";
        var method = Value(payment, "PaymentMeansCode") switch
        {
            "47" => "Transferencia bancaria", "10" => "Efectivo", "48" => "Tarjeta de crédito",
            "49" => "Tarjeta débito", "42" => "Consignación bancaria", "20" => "Cheque", _ => "Instrumento no definido"
        };
        return new(Value(root, "ID"), uuid, qr, Value(root, "IssueDate"), Value(root, "IssueTime"), Value(root, "DocumentCurrencyCode"),
            issuer, tradeName, issuerId, string.Join(" · ", new[] { Deep(Child(supplier, "PhysicalLocation"), "Line"), Deep(Child(supplier, "PhysicalLocation"), "CityName") }.Where(x => x.Length > 0)),
            string.Join(" · ", new[] { Deep(Child(supplier, "Contact"), "Telephone"), Deep(Child(supplier, "Contact"), "ElectronicMail") }.Where(x => x.Length > 0)),
            Deep(Child(customer, "PartyTaxScheme"), "RegistrationName"), PartyId(customer), Deep(Child(customer, "Contact"), "ElectronicMail"),
            form, method, Value(payment, "PaymentDueDate"), Deep(root, "InvoiceAuthorization"),
            period is null ? "" : $"Vigencia {Value(period, "StartDate")} a {Value(period, "EndDate")} · {Value(range, "Prefix")}{Value(range, "From")} a {Value(range, "Prefix")}{Value(range, "To")}",
            Deep(Child(root, "BillingReference"), "ID"), Amount(money, "LineExtensionAmount"),
            root.Elements().Where(x => x.Name.LocalName == "TaxTotal").Sum(x => Amount(x, "TaxAmount")),
            Amount(money, "AllowanceTotalAmount"), Amount(money, "ChargeTotalAmount"), Amount(money, "PrepaidAmount"),
            Amount(money, "PayableRoundingAmount"), total, lines, ReadPayments(snapshot.RootElement), credit);
    }

    private static IReadOnlyList<(string Label, decimal Amount)> ReadPayments(JsonElement snapshot)
    {
        var payments = new List<(string Label, decimal Amount)>();
        if (!snapshot.TryGetProperty("order", out var order)) return payments;
        decimal Number(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var n) ? n : 0;
        bool Present(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null;
        if (order.TryGetProperty("bankPayments", out var bank))
            payments.AddRange(bank.EnumerateArray().Where(x => !Present(x, "SourceReservationDepositId")).Select(x => ("Transferencia bancaria", Number(x, "Amount"))));
        if (order.TryGetProperty("appPayments", out var app)) payments.AddRange(app.EnumerateArray().Select(x => ("Aplicación", Number(x, "Amount"))));
        if (order.TryGetProperty("deposits", out var deposits))
            payments.AddRange(deposits.EnumerateArray().Select(x => (x.TryGetProperty("IsEffective", out var cash) && cash.ValueKind == JsonValueKind.True
                ? "Anticipo en efectivo" : Present(x, "BankId") ? "Anticipo por transferencia" : Present(x, "AppId") ? "Anticipo por aplicación" : "Anticipo registrado", Number(x, "Amount"))));
        payments.Add(("Efectivo en caja", Number(order, "PaidInStoreCashAmount")));
        return payments.Where(x => x.Amount > 0).GroupBy(x => x.Label).Select(x => (x.Key, x.Sum(v => v.Amount))).ToArray();
    }

    public static byte[] Create(ElectronicInvoiceDocument document, ElectronicInvoiceBranding? branding = null)
    {
        var data = Read(document);
        lock (FontLock) { GlobalFontSettings.FontResolver ??= new EmbeddedFonts(); }
        using var pdf = new PdfDocument();
        pdf.Info.Title = $"{(data.Credit ? "Nota crédito" : "Factura electrónica de venta")} {data.Number}";
        pdf.Info.Author = data.Issuer;
        XFont regular = new("InvoiceVera", 9), bold = new("InvoiceVera", 9, XFontStyleEx.Bold);
        XFont small = new("InvoiceVera", 7), title = new("InvoiceVera", 17, XFontStyleEx.Bold);
        XGraphics g = null!;
        using var logo = branding is { Logo.Length: > 0 } ? XImage.FromStream(new MemoryStream(branding.Logo)) : null;
        double y = 0;
        const double left = 36, width = 523, bottom = 650;
        List<string> Wrap(string text, XFont font, double max)
        {
            var result = new List<string>();
            foreach (var paragraph in text.Replace("\r", "").Split('\n'))
            {
                var line = "";
                foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate = line.Length == 0 ? word : line + " " + word;
                    if (g.MeasureString(candidate, font).Width <= max) { line = candidate; continue; }
                    if (line.Length > 0) { result.Add(line); line = ""; }
                    foreach (var ch in word)
                    {
                        if (g.MeasureString(line + ch, font).Width > max && line.Length > 0) { result.Add(line); line = ""; }
                        line += ch;
                    }
                }
                result.Add(line.TrimEnd());
            }
            return result;
        }
        void Text(string text, double x, double top, XFont font, XBrush? brush = null) => g.DrawString(text, font, brush ?? Ink, x, top + font.Size);
        double Block(string text, double x, double top, XFont font, double max, XBrush? brush = null)
        {
            foreach (var line in Wrap(text, font, max)) { Text(line, x, top, font, brush); top += font.Size + 4; }
            return top;
        }
        string Money(decimal amount) => amount.ToString("N2", Colombian);
        void Right(string text, double x, double top, XFont font, XBrush? brush = null) => Text(text, x - g.MeasureString(text, font).Width, top, font, brush);
        void TableHeader()
        {
            g.DrawRectangle(new XSolidBrush(XColor.FromArgb(239, 247, 244)), left, y, width, 26);
            Text("DESCRIPCIÓN", left + 8, y + 7, small); Right("CANT.", 318, y + 7, small);
            Right("VALOR UNIT.", 397, y + 7, small); Right("DESCUENTO", 471, y + 7, small); Right("VALOR TOTAL", 551, y + 7, small);
            y += 34;
        }
        void NewPage(bool continuation)
        {
            g?.Dispose();
            var page = pdf.AddPage(); page.Size = PdfSharp.PageSize.A4;
            g = XGraphics.FromPdfPage(page);
            g.DrawLine(new XPen(XColor.FromArgb(0, 148, 106), 2), left, 32, 559, 32);
            var identityLeft = left;
            if (logo is not null)
            {
                var scale = 76d / Math.Max(logo.PixelWidth, logo.PixelHeight);
                var logoWidth = logo.PixelWidth * scale; var logoHeight = logo.PixelHeight * scale;
                g.DrawImage(logo, left + (76 - logoWidth) / 2, 47 + (76 - logoHeight) / 2, logoWidth, logoHeight);
                identityLeft += 88;
            }
            var displayName = branding?.DisplayName ?? (string.IsNullOrWhiteSpace(data.TradeName) ? data.Issuer : data.TradeName);
            var identityWidth = 330 - (identityLeft - left);
            var identitySize = Math.Clamp(17 * identityWidth * 2 / Math.Max(identityWidth * 2, g.MeasureString(displayName, title).Width), 10, 17);
            y = Block(displayName, identityLeft, 48, new XFont("InvoiceVera", identitySize, XFontStyleEx.Bold), identityWidth);
            if (displayName != data.Issuer) y = Block(data.Issuer, identityLeft, y + 5, small, 330 - (identityLeft - left));
            y = Math.Max(y, logo is null ? y : 115);
            y = Block("NIT " + data.IssuerId, left, y + 4, regular, 330);
            if (data.IssuerAddress.Length > 0) y = Block(data.IssuerAddress, left, y + 2, small, 330, Muted);
            if (data.IssuerContact.Length > 0) y = Block(data.IssuerContact, left, y + 2, small, 330, Muted);
            Text(data.Credit ? "NOTA CRÉDITO ELECTRÓNICA" : "FACTURA ELECTRÓNICA DE VENTA", 378, 49, small, Green);
            var numberSize = Math.Clamp(17 * 177 / Math.Max(177, g.MeasureString(data.Number, title).Width), 8, 17);
            Text(data.Number, 378, 67, new XFont("InvoiceVera", numberSize, XFontStyleEx.Bold), Green);
            Text($"{data.Date} · {data.Time}", 378, 94, small, Muted);
            Text(document.Environment == "production" ? "Validada por la DIAN" : "AMBIENTE DE PRUEBAS", 378, 111, small, Green);
            y = Math.Max(143, y + 17);
            var receiverBottom = Block(data.Customer, left + 9, y + 19, bold, 310);
            receiverBottom = Block(data.CustomerId, left + 9, receiverBottom + 3, regular, 310);
            if (data.CustomerContact.Length > 0) receiverBottom = Block(data.CustomerContact, left + 9, receiverBottom + 3, small, 310, Muted);
            Text("CLIENTE", left + 9, y + 3, small, Muted);
            Text("Forma: " + data.Form, 386, y + 5, regular);
            var paymentBottom = Block("Medio: " + data.Method, 386, y + 24, regular, 162);
            if (data.Due.Length > 0) paymentBottom = Block("Vence: " + data.Due, 386, paymentBottom + 3, small, 162, Muted);
            y = Math.Max(receiverBottom, paymentBottom) + 17;
            if (data.Reference.Length > 0) y = Block("Documento de referencia: " + data.Reference, left, y, regular, width) + 8;
            if (continuation) { Text("Detalle (continuación)", left, y, small, Muted); y += 20; }
            TableHeader();
        }
        NewPage(false);
        foreach (var line in data.Lines)
        {
            var description = Wrap(line.Name, regular, 238);
            var height = Math.Max(28, description.Count * 13 + 12);
            if (y + height > bottom) NewPage(true);
            Block(line.Name, left + 8, y, regular, 238);
            Right(line.Quantity.ToString("0.##", Colombian), 318, y, regular);
            Right(Money(line.UnitPrice), 397, y, regular);
            Right(line.Discount > 0 ? Money(line.Discount) : "-", 471, y, regular);
            Right(Money(line.Net), 551, y, bold);
            y += height;
            g.DrawLine(new XPen(XColor.FromArgb(226, 233, 237), .5), left, y - 6, 559, y - 6);
        }
        var totals = new List<(string, decimal)> { ("Subtotal", data.Net) };
        if (data.Discount != 0) totals.Add(("Descuento global", -data.Discount));
        if (data.Charges != 0) totals.Add(("Cargos", data.Charges));
        totals.Add(("Impuestos", data.Taxes));
        if (data.Prepaid != 0) totals.Add(("Anticipos fiscales", -data.Prepaid));
        if (data.Rounding != 0) totals.Add(("Redondeo", data.Rounding));
        if (y + Math.Max(totals.Count * 22 + 75, data.Payments.Count * 18 + 74) > bottom) NewPage(true);
        y += 12;
        var paymentsBottom = y;
        if (data.Payments.Count > 0)
        {
            var py = y;
            Text("PAGOS REGISTRADOS EN EL PEDIDO", left, py, small, Muted); py += 20;
            foreach (var payment in data.Payments)
            {
                Text(payment.Label, left, py, regular); Right(Money(payment.Amount), 315, py, regular); py += 18;
            }
            Text("Información del pedido; no modifica el medio fiscal del XML.", left, py + 4, new XFont("InvoiceVera", 6), Muted);
            paymentsBottom = py + 15;
        }
        foreach (var (label, amount) in totals) { Text(label, 343, y, regular, Muted); Right(Money(amount), 551, y, regular); y += 22; }
        g.DrawRectangle(new XSolidBrush(XColor.FromArgb(231, 247, 240)), 332, y, 227, 37);
        Text("TOTAL " + data.Currency, 343, y + 11, bold, Green); Right("$ " + Money(data.Total), 549, y + 11, bold, Green);
        y = Math.Max(y + 52, paymentsBottom + 15);
        Block("Gracias por su compra.", left, y, regular, 260, Muted);
        g.Dispose();
        using var qrData = QRCodeGenerator.GenerateQrCode(data.Qr, QRCodeGenerator.ECCLevel.M);
        for (var index = 0; index < pdf.Pages.Count; index++)
        {
            using var footer = XGraphics.FromPdfPage(pdf.Pages[index], XGraphicsPdfPageOptions.Append);
            g = footer;
            g.DrawLine(new XPen(XColor.FromArgb(215, 227, 224), .7), left, 675, 559, 675);
            var modules = qrData.ModuleMatrix.Count;
            var unit = 112d / modules;
            for (var row = 0; row < modules; row++)
                for (var col = 0; col < modules; col++)
                    if (qrData.ModuleMatrix[row][col]) g.DrawRectangle(XBrushes.Black, left + col * unit, 688 + row * unit, unit + .02, unit + .02);
            double fy = 691;
            fy = Block(data.Credit ? "CUDE" : "CUFE", 161, fy, bold, 398);
            fy = Block(data.Uuid, 161, fy + 2, small, 398);
            if (data.Resolution.Length > 0) fy = Block("Resolución DIAN " + data.Resolution, 161, fy + 5, small, 398, Muted);
            if (data.Authorization.Length > 0) fy = Block(data.Authorization, 161, fy + 2, small, 398, Muted);
            Block("Representación gráfica del XML validado. Consulte el documento en la DIAN escaneando el QR.", 161, fy + 5, small, 398, Muted);
            Text($"Página {index + 1} de {pdf.Pages.Count}", left, 813, small, Muted);
            Right(data.Number, 559, 813, small, Muted);
        }
        using var output = new MemoryStream(); pdf.Save(output, false); return output.ToArray();
    }

    private sealed class EmbeddedFonts : IFontResolver
    {
        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new(bold ? "VeraBd" : "Vera");
        public byte[] GetFont(string faceName)
        {
            using var stream = typeof(InvoicePdf).Assembly.GetManifestResourceStream($"SenorArroz.Infrastructure.Assets.Fonts.{faceName}.ttf")!;
            using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray();
        }
    }
}
