using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Integrations;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Controllers;

[ApiController]
[Route("api/electronic-invoicing")]
[Authorize(Roles = "Superadmin,Admin,Cashier")]
public sealed class ElectronicInvoicingController(IApplicationDbContext db, ICurrentUser user, IBranchContext branch,
    IIntegrationSecretProtector secrets, IEnumerable<IElectronicInvoiceProvider> providers,
    IElectronicInvoiceService service, IClock clock, IConfiguration configuration) : ControllerBase
{
    private IElectronicInvoiceProvider Provider(ElectronicInvoiceIntegration i) => providers.Single(p => p.Code == i.Provider);
    private IActionResult Success(object? value) => Ok(ApiResponse<object>.SuccessResponse(value!));

    [HttpGet("availability")]
    public async Task<IActionResult> Availability(CancellationToken ct) => Success(new { enabled = await db.ElectronicInvoiceIntegrations.AnyAsync(x => x.IsActive && (x.Environment != "production" || ProductionEnabled), ct), productionEnabled = ProductionEnabled });

    private bool ProductionEnabled => configuration.GetValue<bool>("ElectronicInvoicing:ProductionEnabled");

    [HttpGet("integrations")]
    [Authorize(Roles = "Superadmin")]
    public async Task<IActionResult> Integrations(CancellationToken ct)
    {
        var integrations = await db.ElectronicInvoiceIntegrations.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        return Success(integrations.Select(IntegrationDto));
    }

    [HttpPost("integrations")]
    [Authorize(Roles = "Superadmin")]
    public async Task<IActionResult> Save(InvoiceIntegrationDto dto, CancellationToken ct)
    {
        if (dto.Environment is not ("sandbox" or "production")) throw new BusinessException("Ambiente inválido.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({user.TenantId}, -781)", ct);
        if (await db.ElectronicInvoiceIntegrations.AnyAsync(x => x.Id != dto.Id && x.TaxId != dto.TaxId.Trim(), ct))
            throw new BusinessException("Las integraciones del negocio deben corresponder al mismo NIT emisor.");
        var i = dto.Id.HasValue ? await db.ElectronicInvoiceIntegrations.SingleOrDefaultAsync(x => x.Id == dto.Id, ct)
            ?? throw new NotFoundException("Integración no encontrada") : new ElectronicInvoiceIntegration();
        var used = i.Id > 0 && await db.ElectronicInvoiceDocuments.AnyAsync(x => x.IntegrationId == i.Id, ct);
        if (used && (i.Environment != dto.Environment || i.TaxId != dto.TaxId || i.Prefix != dto.Prefix
            || i.Resolution != dto.Resolution || i.CreditPrefix != dto.CreditPrefix
            || i.NextNumber != dto.NextNumber || i.NextCreditNumber != dto.NextCreditNumber))
            throw new BusinessException("Esta integración ya tiene documentos. Conserva ambiente, emisor y consecutivos; registra otra para cambiar la numeración.");
        if (i.IsActive) throw new BusinessException("Desactiva la integración antes de cambiar su configuración.");
        if (i.Environment != dto.Environment || i.TaxId != dto.TaxId.Trim()) i.TestedAt = null;
        if (i.Environment != dto.Environment || i.Resolution != dto.Resolution.Trim() || i.Prefix != dto.Prefix.Trim()) i.ResolutionsJson = null;
        i.Name = dto.Name.Trim(); i.Environment = dto.Environment;
        i.TaxId = dto.TaxId.Trim(); i.LegalName = dto.LegalName.Trim(); i.BillingEmail = dto.BillingEmail.Trim();
        i.Prefix = dto.Prefix.Trim(); i.Resolution = dto.Resolution.Trim();
        i.NextNumber = dto.NextNumber; i.LastNumber = dto.LastNumber;
        i.ValidUntil = dto.ValidUntil?.ToUniversalTime(); i.CreditPrefix = dto.CreditPrefix.Trim(); i.NextCreditNumber = dto.NextCreditNumber;
        i.ProductionReady = dto.ProductionReady;
        if (!string.IsNullOrWhiteSpace(dto.Token)) { i.EncryptedToken = secrets.Protect(dto.Token.Trim()); i.TestedAt = null; i.ResolutionsJson = null; }
        if (i.Id == 0) db.ElectronicInvoiceIntegrations.Add(i);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Success(IntegrationDto(i));
    }

    [HttpPost("integrations/{id:int}/test")]
    [Authorize(Roles = "Superadmin")]
    public async Task<IActionResult> Test(int id, CancellationToken ct)
    {
        var i = await Integration(id, ct);
        var company = await Provider(i).CompanyAsync(i, ct);
        var remoteTaxId = PlemsiInvoiceProvider.CompanyTaxId(company);
        if (string.IsNullOrWhiteSpace(remoteTaxId) || remoteTaxId != i.TaxId)
            throw new BusinessException("El NIT configurado no coincide con la empresa del token. Verifica la respuesta de empresa con Plemsi.");
        i.CompanyJson = company.GetRawText();
        i.TestedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return Success(new { company, testedAt = i.TestedAt });
    }

    [HttpPost("integrations/{id:int}/resolutions")]
    [Authorize(Roles = "Superadmin")]
    public async Task<IActionResult> Resolutions(int id, CancellationToken ct)
    {
        var i = await Integration(id, ct);
        var result = await Provider(i).ResolutionsAsync(i, ct);
        JsonElement? remaining = string.IsNullOrWhiteSpace(i.Resolution) ? null : await Provider(i).RemainingAsync(i, i.Resolution, ct);
        i.ResolutionsJson = JsonSerializer.Serialize(new { resolutions = result, remaining });
        await db.SaveChangesAsync(ct);
        return Success(new { resolutions = InvoiceNumbering.Read(result), remaining, nextNumber = remaining.HasValue ? InvoiceNumbering.ReadNext(remaining.Value) : null });
    }

    [HttpPost("integrations/{id:int}/email")]
    [Authorize(Roles = "Superadmin")]
    public async Task<IActionResult> SyncEmail(int id, CancellationToken ct)
    {
        var i = await Integration(id, ct);
        await Provider(i).SyncEmailAsync(i, ct);
        return Success(new { synced = true, customSenderSupported = false });
    }

    [HttpPost("integrations/{id:int}/activation")]
    [Authorize(Roles = "Superadmin")]
    public async Task<IActionResult> Activate(int id, ActivationDto dto, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({user.TenantId}, -781)", ct);
        var i = await Integration(id, ct);
        if (dto.Enabled)
        {
            if (i.Environment == "production" && !ProductionEnabled) throw new BusinessException("La emisión real está desactivada en este servidor mientras se completan las pruebas con Plemsi.");
            ElectronicInvoiceService.EnsureReady(i);
            var resolutions = await Provider(i).ResolutionsAsync(i, ct);
            var remaining = await Provider(i).RemainingAsync(i, i.Resolution, ct);
            InvoiceNumbering.Validate(i, InvoiceNumbering.Read(resolutions), remaining, clock.UtcNow, dto.NumberingConfirmed);
            if (await db.ElectronicInvoiceDocuments.AnyAsync(x => x.Environment == i.Environment
                && x.Kind == "invoice" && x.Prefix == i.Prefix && x.Number >= i.NextNumber, ct))
                throw new BusinessException("El consecutivo ya fue utilizado o reservado en este negocio. Configura uno superior al último registrado.");
            i.ResolutionsJson = JsonSerializer.Serialize(new { resolutions, remaining, numberingConfirmed = dto.NumberingConfirmed, confirmedBy = user.Id, confirmedAt = clock.UtcNow });
            var others = await db.ElectronicInvoiceIntegrations.Where(x => x.IsActive && x.Id != id).ToListAsync(ct);
            foreach (var other in others) other.IsActive = false;
            await db.SaveChangesAsync(ct);
        }
        i.IsActive = dto.Enabled;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Success(IntegrationDto(i));
    }

    [HttpGet("customers/{customerId:int}/recipients")]
    public async Task<IActionResult> Recipients(int customerId, CancellationToken ct)
    {
        await Customer(customerId, ct);
        return Success(await db.CustomerBillingRecipients.AsNoTracking().Where(x => x.CustomerId == customerId)
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name).Select(x => new { x.Id, x.DocumentType, x.Identification, x.Name, x.Email, x.IsDefault }).ToListAsync(ct));
    }

    [HttpPost("customers/{customerId:int}/recipients")]
    public async Task<IActionResult> SaveRecipient(int customerId, BillingRecipientDto dto, CancellationToken ct)
    {
        ElectronicInvoiceService.ValidateRecipient(dto);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Customer(customerId, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({user.TenantId}, {customerId})", ct);
        var r = dto.Id.HasValue ? await db.CustomerBillingRecipients.SingleOrDefaultAsync(x => x.Id == dto.Id && x.CustomerId == customerId, ct)
            ?? throw new NotFoundException("Destinatario no encontrado")
            : await db.CustomerBillingRecipients.SingleOrDefaultAsync(x => x.CustomerId == customerId && x.DocumentType == dto.DocumentType && x.Identification == dto.Identification, ct)
                ?? new CustomerBillingRecipient { CustomerId = customerId };
        var any = await db.CustomerBillingRecipients.AnyAsync(x => x.CustomerId == customerId, ct);
        if (dto.IsDefault || !any)
        {
            var previous = await db.CustomerBillingRecipients.Where(x => x.CustomerId == customerId && x.IsDefault).ToListAsync(ct);
            foreach (var p in previous) p.IsDefault = false;
            await db.SaveChangesAsync(ct);
        }
        r.DocumentType = dto.DocumentType; r.Identification = dto.Identification.Trim(); r.Name = dto.Name.Trim();
        r.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim(); r.IsDefault = dto.IsDefault || !any;
        if (r.Id == 0) db.CustomerBillingRecipients.Add(r);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Success(new { r.Id, r.DocumentType, r.Identification, r.Name, r.Email, r.IsDefault });
    }

    [HttpGet("recipient-lookup")]
    public async Task<IActionResult> Lookup([FromQuery, Range(1, 99)] int type, [FromQuery, Required, MaxLength(32)] string identification, CancellationToken ct)
    {
        var i = await db.ElectronicInvoiceIntegrations.SingleOrDefaultAsync(x => x.IsActive, ct)
            ?? throw new BusinessException("Activa un proveedor para consultar la DIAN.");
        return Success(await Provider(i).LookupRecipientAsync(i, type, identification, ct));
    }

    [HttpGet("orders/{orderId:int}")]
    public async Task<IActionResult> Documents(int orderId, CancellationToken ct)
    {
        await Order(orderId, ct);
        var documents = await db.ElectronicInvoiceDocuments.AsNoTracking().Where(x => x.OrderId == orderId).OrderByDescending(x => x.Id)
            .Select(x => new { x.Id, x.Kind, x.Status, x.Environment, x.Prefix, x.Number, x.Cufe, x.EmailStatus, x.CreditRequested,
                x.LastError, x.IssuedAt, x.OriginalDocumentId, x.Reason, x.RecipientJson, hasPdf = x.Pdf != null, hasXml = x.Xml != null }).ToListAsync(ct);
        return Success(documents);
    }

    [HttpPost("orders/{orderId:int}")]
    public async Task<IActionResult> RequestInvoice(int orderId, BillingRecipientDto dto, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM public.\"order\" WHERE id = {orderId} AND tenant_id = {user.TenantId} FOR UPDATE", ct);
        var order = await Order(orderId, ct);
        var document = await service.RequestAsync(order, dto, user.Id, ct);
        await tx.CommitAsync(ct);
        return Success(new { document.Id, document.Status });
    }

    [HttpPost("documents/{id:int}/retry")]
    public async Task<IActionResult> Retry(int id, BillingRecipientDto? recipient, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var orderId = await db.ElectronicInvoiceDocuments.Where(x => x.Id == id).Select(x => (int?)x.OrderId).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Documento no encontrado");
        await Order(orderId, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM public.\"order\" WHERE id = {orderId} AND tenant_id = {user.TenantId} FOR UPDATE", ct);
        var d = await Document(id, ct);
        if (d.LeaseUntil > clock.UtcNow) throw new BusinessException("El documento está siendo procesado.");
        if (d.Status == "rejected")
        {
            if (recipient is not null && d.Kind == "invoice")
            {
                ElectronicInvoiceService.ValidateRecipient(recipient);
                d.RecipientJson = JsonSerializer.Serialize(recipient);
                d.EmailStatus = string.IsNullOrWhiteSpace(recipient.Email) ? "notRequested" : "pending";
            }
            if (d.Kind == "invoice" && d.Number.HasValue)
            {
                var order = await db.Orders.Include(x => x.OrderDetails).ThenInclude(x => x.Product)
                    .Include(x => x.BankPayments).Include(x => x.AppPayments).Include(x => x.Deposits).SingleAsync(x => x.Id == d.OrderId, ct);
                var integration = await Integration(d.IntegrationId, ct);
                d.PayloadJson = ElectronicInvoiceService.BuildInvoicePayload(order, JsonSerializer.Deserialize<BillingRecipientDto>(d.RecipientJson)!, integration, d.Prefix, d.Number.Value, clock.UtcNow);
                d.SnapshotJson = ElectronicInvoiceService.BuildSnapshot(order, integration);
            }
            d.SubmittedAt = null; d.Status = "pending";
        }
        d.NextAttemptAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Success(new { d.Status });
    }

    [HttpPost("documents/{id:int}/email")]
    public async Task<IActionResult> Resend(int id, CancellationToken ct)
    {
        var d = await Document(id, ct);
        if (d.Status is not ("issued" or "credited")) throw new BusinessException("Espera la validación DIAN antes de enviar el correo.");
        var recipient = JsonSerializer.Deserialize<BillingRecipientDto>(d.RecipientJson)!;
        if (string.IsNullOrWhiteSpace(recipient.Email)) throw new BusinessException("El documento no tiene correo de entrega.");
        var i = await Integration(d.IntegrationId, ct);
        await Provider(i).SendEmailAsync(i, d.Cufe!, recipient.Email, ct);
        d.EmailStatus = "accepted"; await db.SaveChangesAsync(ct);
        return Success(new { d.EmailStatus });
    }

    [HttpGet("documents/{id:int}/{format:regex(^(pdf|xml)$)}")]
    public async Task<IActionResult> Download(int id, string format, CancellationToken ct)
    {
        var d = await Document(id, ct);
        var bytes = format == "pdf" ? d.Pdf : d.Xml;
        if (bytes is null) throw new BusinessException("El archivo todavía no está disponible.");
        return File(bytes, format == "pdf" ? "application/pdf" : "application/xml", $"{d.Prefix}{d.Number}.{format}");
    }

    [HttpPost("documents/{id:int}/credit")]
    [Authorize(Roles = "Superadmin,Admin")]
    public async Task<IActionResult> Credit(int id, CreditDto dto, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM electronic_invoice_document WHERE id = {id} AND tenant_id = {user.TenantId} FOR UPDATE", ct);
        var d = await Document(id, ct);
        if (d.Kind != "invoice" || d.Status != "issued") throw new BusinessException("Solo se puede anular una factura validada.");
        var previous = await db.ElectronicInvoiceDocuments.FirstOrDefaultAsync(x => x.OriginalDocumentId == id, ct);
        if (previous is not null) return Success(new { previous.Id, previous.Status });
        var credit = new ElectronicInvoiceDocument { TenantId = d.TenantId, OrderId = d.OrderId, IntegrationId = d.IntegrationId,
            OriginalDocumentId = d.Id, Kind = "credit", Environment = d.Environment, RecipientJson = d.RecipientJson,
            Reason = dto.Reason.Trim(), RequestedById = user.Id, AuthorizedById = user.Id,
            EmailStatus = JsonSerializer.Deserialize<BillingRecipientDto>(d.RecipientJson)!.Email is null ? "notRequested" : "pending" };
        db.ElectronicInvoiceDocuments.Add(credit); d.CreditRequested = true;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Success(new { credit.Id, credit.Status });
    }

    private async Task<ElectronicInvoiceIntegration> Integration(int id, CancellationToken ct) =>
        await db.ElectronicInvoiceIntegrations.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Integración no encontrada");
    private async Task Customer(int id, CancellationToken ct)
    {
        if (!await db.Customers.AnyAsync(x => x.Id == id, ct)) throw new NotFoundException("Cliente no encontrado");
    }
    private async Task<Order> Order(int id, CancellationToken ct)
    {
        var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Pedido no encontrado");
        OrderAccessRules.EnsureAccess(order, user, branch);
        return order;
    }
    private async Task<ElectronicInvoiceDocument> Document(int id, CancellationToken ct)
    {
        var d = await db.ElectronicInvoiceDocuments.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Documento no encontrado");
        await Order(d.OrderId, ct);
        return d;
    }
    private object IntegrationDto(ElectronicInvoiceIntegration i) => new { i.Id, i.Name, i.Provider, i.Environment, i.IsActive, productionEnabled = ProductionEnabled,
        i.TaxId, i.LegalName, i.BillingEmail, i.Resolution, i.Prefix, i.NextNumber, i.LastNumber, i.ValidUntil,
        i.CreditPrefix, i.NextCreditNumber, i.ProductionReady, i.TestedAt, hasToken = !string.IsNullOrWhiteSpace(i.EncryptedToken), customSenderSupported = false };
}

public sealed record InvoiceIntegrationDto
{
    public int? Id { get; init; }
    [Required, MaxLength(100)] public string Name { get; init; } = "Plemsi";
    [Required] public string Environment { get; init; } = "sandbox";
    [MaxLength(4096)] public string? Token { get; init; }
    [Required, MaxLength(32)] public string TaxId { get; init; } = "";
    [Required, MaxLength(200)] public string LegalName { get; init; } = "";
    [Required, EmailAddress, MaxLength(254)] public string BillingEmail { get; init; } = "";
    [MaxLength(64)] public string Resolution { get; init; } = "";
    [MaxLength(16)] public string Prefix { get; init; } = "";
    [Range(1, long.MaxValue)] public long NextNumber { get; init; } = 1;
    [Range(0, long.MaxValue)] public long LastNumber { get; init; }
    public DateTime? ValidUntil { get; init; }
    [MaxLength(16)] public string CreditPrefix { get; init; } = "NC";
    [Range(1, long.MaxValue)] public long NextCreditNumber { get; init; } = 1;
    public bool ProductionReady { get; init; }
}
public sealed record ActivationDto(bool Enabled, bool NumberingConfirmed = false);
public sealed record CreditDto([Required, MinLength(5), MaxLength(500)] string Reason);
