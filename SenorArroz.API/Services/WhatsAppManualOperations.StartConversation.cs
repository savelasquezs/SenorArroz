using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Services;
using SenorArroz.Shared.Models;

namespace SenorArroz.API.Services;

public sealed partial class WhatsAppManualOperations
{
    private const string StarterLanguage = "es_CO";
    private const string MenuDocumentTemplate = "carta_solicitada";
    private const string MenuImageTemplate = "carta_imagen_solicitada";
    private const string DeliveryUpdateTemplate = "novedad_entrega_domiciliario";
    private const string MenuDocumentType = "application/pdf";
    private const int MaxMenuImageBytes = 10_000_000;
    private static readonly CultureInfo Colombia = CultureInfo.GetCultureInfo("es-CO");
    private static readonly (string Key, string Name)[] StarterTemplates =
    [
        ("menu", MenuDocumentTemplate),
        ("catalog", "catalogo_solicitado"),
        ("payment", "pago_pedido_bancolombia"),
        ("delivery_update", DeliveryUpdateTemplate)
    ];

    internal static bool IsStarterOnlyTemplate(string name, string language) =>
        language == StarterLanguage && name != DeliveryUpdateTemplate
        && (name == MenuImageTemplate || StarterTemplates.Any(template => template.Name == name));

    public async Task<ActionResult<ApiResponse<WhatsAppConversationStartOptions>>> GetConversationStartOptionsAsync(
        int branchId, CancellationToken ct)
    {
        _branchContext.EnsureAccess(branchId);
        var sender = await ResolveConversationStarterAsync(branchId, ct);
        if (sender is null)
            return BadRequest(ApiResponse<WhatsAppConversationStartOptions>.ErrorResponse("La sucursal no tiene una línea de WhatsApp activa y verificada."));

        var response = await _whatsAppCloudClient.GetMessageTemplatesAsync(sender.BusinessAccountId, sender.AccessToken, ct);
        if (!response.Success)
            return BadRequest(ApiResponse<WhatsAppConversationStartOptions>.ErrorResponse("No se pudieron consultar las plantillas de esta línea en Meta."));

        var account = TransferAccount(branchId);
        var options = StarterTemplates.Select(choice =>
        {
            var template = FindStarterTemplate(response, choice.Name, sender.Branch);
            return new WhatsAppConversationStartTemplate(choice.Key, template?.Name ?? choice.Name, template?.Status ?? "NOT_FOUND",
                template is null ? null : ExtractBodyText(template.ComponentsJson),
                StarterUnavailableReason(sender.Branch, choice.Key, account, template));
        }).ToArray();

        return Ok(ApiResponse<WhatsAppConversationStartOptions>.SuccessResponse(new(
            branchId, sender.Branch.Name, sender.PhoneNumberId, sender.DisplayPhoneNumber, account?.Account, account?.Holder, options)));
    }

    public async Task<ActionResult<ApiResponse<WhatsAppConversationStartResult>>> StartConversationAsync(
        WhatsAppConversationStartRequest request, CancellationToken ct)
    {
        ActionResult<ApiResponse<WhatsAppConversationStartResult>> Invalid(string message) =>
            BadRequest(ApiResponse<WhatsAppConversationStartResult>.ErrorResponse(message));

        _branchContext.EnsureAccess(request.BranchId);
        if (!request.RequestedByCustomer)
            return Invalid(request.Template == "delivery_update"
                ? "Confirma que el cliente autorizó recibir avisos de su pedido por WhatsApp."
                : "Confirma que el cliente solicitó recibir este mensaje por WhatsApp.");
        var phone = NormalizeStarterPhone(request.Phone);
        if (phone is null)
            return Invalid("Ingresa un celular colombiano de diez dígitos o un número con código de país.");
        var choice = StarterTemplates.FirstOrDefault(x => x.Key == request.Template);
        if (choice.Name is null)
            return Invalid("Selecciona una plantilla de atención.");
        var sender = await ResolveConversationStarterAsync(request.BranchId, ct);
        if (sender is null || sender.PhoneNumberId != request.SenderId)
            return Invalid("La línea cambió. Vuelve a cargar las plantillas de la sucursal.");

        await using var transaction = _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(ct) : null;
        await LockTemplateRecipientAsync(sender.PhoneNumberId, phone, ct);
        var existingId = await StarterConversations(sender, phone)
            .Select(x => (int?)x.Id).SingleOrDefaultAsync(ct);
        if (existingId is int id) await WhatsAppCommercialDraft.LockAsync(_db, TenantId, id, ct);
        var conversation = existingId is int found
            ? await _db.WhatsAppConversations.SingleOrDefaultAsync(x => x.Id == found, ct)
            : null;
        if (conversation is not null)
        {
            if (!await CanAccessConversationAsync(conversation, ct)) return Forbid();
            if (!WhatsAppAttentionService.CanSendTemplate(conversation, _currentUser.Id))
                return Ok(ApiResponse<WhatsAppConversationStartResult>.SuccessResponse(new(conversation.Id, false)));
            if (conversation.OperationalBranchId is int operational && operational != request.BranchId)
                return Invalid("La conversación se atiende desde otra sucursal. Usa esa sede para preparar el mensaje.");
        }

        var templates = await _whatsAppCloudClient.GetMessageTemplatesAsync(sender.BusinessAccountId, sender.AccessToken, ct);
        if (!templates.Success) return Invalid("No se pudieron consultar las plantillas de esta línea en Meta.");
        var template = FindStarterTemplate(templates, choice.Name, sender.Branch);
        var account = TransferAccount(request.BranchId);
        var unavailable = StarterUnavailableReason(sender.Branch, choice.Key, account, template);
        if (unavailable is not null) return Invalid(unavailable);
        var templateName = template!.Name;
        if (request.TemplateName is not null && request.TemplateName != templateName)
            return Invalid("La plantilla cambió. Vuelve a abrir Nueva conversación para revisar el formato actual.");

        string[] parameters;
        if (choice.Key == "payment")
        {
            if (request.Amount is not > 0)
                return Invalid("Ingresa un total a pagar mayor que cero.");
            var amount = request.Amount.Value.ToString("N0", Colombia);
            parameters = [account!.Account.Trim(), account.Holder.Trim(), $"${amount} COP"];
        }
        else if (choice.Key == "delivery_update")
        {
            var update = Regex.Replace(request.DeliveryUpdate?.Trim() ?? string.Empty, @"\s+", " ");
            if (string.IsNullOrEmpty(update))
                return Invalid("Escribe la novedad del domiciliario.");
            parameters = [update];
        }
        else
        {
            parameters = CountBodyTemplateParameters(template.ComponentsJson) == 1 ? [sender.Branch.Name] : [];
        }

        var text = BuildTemplateMessagePreview(new WhatsAppTemplate { Name = templateName, Components = template.ComponentsJson }, parameters);
        if (choice.Key == "delivery_update" && text.Length > 1024)
            return Invalid("Acorta la novedad para que quepa en la plantilla de WhatsApp.");

        string? documentId = null, mediaUrl = null, documentName = null;
        if (templateName == MenuImageTemplate)
        {
            mediaUrl = MenuImages(sender.Branch)[0];
        }
        else if (templateName == MenuDocumentTemplate)
        {
            documentName = $"Carta Señor Arroz - {sender.Branch.Name}.pdf";
            (documentId, mediaUrl) = await PrepareStarterMenuAsync(sender, documentName, ct);
            if (documentId is null)
                return Invalid("No se pudo preparar el PDF con las imágenes de la carta de esta sucursal.");
        }
        var components = new List<object>();
        if (parameters.Length > 0)
            components.Add(new { type = "body", parameters = parameters.Select(text => new { type = "text", text }).ToArray() });
        if (documentId is not null)
            components.Add(new { type = "header", parameters = new[]
                { new { type = "document", document = new { id = documentId, filename = documentName } } } });
        if (templateName == MenuImageTemplate)
            components.Add(new { type = "header", parameters = new[]
                { new { type = "image", image = new { link = mediaUrl } } } });
        if (choice.Key == "catalog")
            components.Add(new { type = "button", sub_type = "catalog", index = 0, parameters = new[]
                { new { type = "action", action = new Dictionary<string, string>() } } });

        var timestamp = _clock.UtcNow;
        if (conversation is null)
        {
            var customer = await _webhookProcessor.FindUniqueCustomerByPhoneAsync(phone, ct);
            conversation = new WhatsAppConversation
            {
                TenantId = TenantId, BranchId = request.BranchId,
                OperationalBranchId = sender.ChannelId.HasValue ? request.BranchId : null,
                ChannelSettingId = sender.ChannelId, BranchLineId = sender.BranchLineId, PhoneNumber = phone,
                CustomerId = customer?.Id, ContactName = customer?.Name, WhatsAppUsername = customer?.WhatsAppUsername,
                Status = WhatsAppConversationStatus.Open, AttentionMode = WhatsAppAttentionMode.Human,
                AssignedUserId = _currentUser.Id, HumanAssignedAt = timestamp,
                AttentionModeUpdatedAt = timestamp, AttentionModeUpdatedByUserId = _currentUser.Id, AttentionRevision = 1
            };
            _db.WhatsAppConversations.Add(conversation);
            if (transaction is not null) await _db.SaveChangesAsync(ct);
        }

        var payload = JsonSerializer.SerializeToElement(new { name = templateName, language = new { code = StarterLanguage }, components });
        var sent = await _whatsAppCloudClient.SendSharedContentAsync(sender.PhoneNumberId, sender.AccessToken, phone, "template", payload, ct);
        RequireKnownOutcome(sent);
        if (sent.OutcomeUncertain)
            return StatusCode(502, ApiResponse<WhatsAppConversationStartResult>.ErrorResponse("No se pudo confirmar el envío. Revisa la conversación antes de repetirlo."));
        if (!sent.Success)
            return Invalid(sent.ErrorMessage ?? "Meta rechazó el envío de la plantilla.");

        _attentionService.AssignAfterTemplate(conversation, _currentUser.Id, _clock.UtcNow);
        var message = new WhatsAppMessage
        {
            TenantId = TenantId, Conversation = conversation, WhatsAppMessageId = sent.WhatsAppMessageId,
            Direction = WhatsAppMessageDirection.Outbound,
            Type = templateName == MenuImageTemplate ? WhatsAppMessageType.Image
                : documentId is not null ? WhatsAppMessageType.Document : WhatsAppMessageType.Text,
            TextBody = text, MediaId = documentId, MediaUrl = mediaUrl, MediaMimeType = documentId is null ? null : MenuDocumentType,
            MediaFileName = documentName,
            Status = WhatsAppMessageStatus.Sent, Timestamp = _clock.UtcNow, SentByUserId = _currentUser.Id,
            RawPayload = JsonSerializer.Serialize(new { action = "start_conversation", template = templateName, language = StarterLanguage })
        };
        conversation.LastMessageAt = message.Timestamp;
        conversation.LastMessagePreview = $"Plantilla: {templateName}";
        _db.WhatsAppMessages.Add(message);
        await _db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        try { await NotifyWhatsAppMessageCreatedAsync(message.Id, ct); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _logger.LogWarning("No se pudo notificar el nuevo mensaje. MessageId={MessageId} ErrorType={ErrorType}",
                message.Id, error.GetType().Name);
        }
        return Ok(ApiResponse<WhatsAppConversationStartResult>.SuccessResponse(new(conversation.Id, true)));
    }

    internal async Task<bool> CanStartOrderConversationAsync(int branchId, string phone, CancellationToken ct)
    {
        var sender = await ResolveConversationStarterAsync(branchId, ct);
        return sender is not null && !await StarterConversations(sender, phone).AnyAsync(ct);
    }

    private IQueryable<WhatsAppConversation> StarterConversations(ConversationStarter sender, string phone) =>
        _db.WhatsAppConversations.AsNoTracking().Where(x => x.TenantId == TenantId && x.PhoneNumber == phone
            && x.ChannelSettingId == sender.ChannelId && x.BranchLineId == sender.BranchLineId
            && (sender.ChannelId != null || x.BranchId == sender.Branch.Id));

    private async Task<ConversationStarter?> ResolveConversationStarterAsync(int branchId, CancellationToken ct)
    {
        var branch = await _db.Branches.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == branchId && x.TenantId == TenantId && x.IsActive, ct);
        if (branch is null) return null;
        var line = await _db.WhatsAppBranchSettings.AsNoTracking()
            .Where(x => x.BranchId == branchId && x.TenantId == TenantId && x.IsActive && x.IsVerified
                && x.PhoneNumberId != "" && x.BusinessAccountId != "" && x.AccessToken != "")
            .OrderByDescending(x => x.CoexistenceEnabled).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (line is not null)
            return new(branch, null, line.CoexistenceEnabled ? line.Id : null,
                line.PhoneNumberId, line.BusinessAccountId, line.AccessToken, line.DisplayPhoneNumber);
        var central = await _db.WhatsAppChannelSettings.AsNoTracking()
            .Where(x => x.TenantId == TenantId && x.IsActive && x.IsVerified
                && x.PhoneNumberId != "" && x.BusinessAccountId != "" && x.AccessToken != "")
            .OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        return central is null ? null : new(branch, central.Id, null,
            central.PhoneNumberId, central.BusinessAccountId, central.AccessToken, central.DisplayPhoneNumber);
    }

    private WhatsAppBranchTransferAccount? TransferAccount(int branchId) =>
        _whatsAppOptions.BancolombiaByBranch.TryGetValue(branchId, out var account)
        && !string.IsNullOrWhiteSpace(account.Account) && !string.IsNullOrWhiteSpace(account.Holder) ? account : null;

    private static WhatsAppCloudTemplate? FindStarterTemplate(WhatsAppCloudTemplateSyncResult result, string name, Branch branch)
    {
        var standard = result.Templates.FirstOrDefault(x => x.Name == name && x.Language == StarterLanguage);
        if (name != MenuDocumentTemplate || MenuImages(branch).Length != 1)
            return standard;

        var image = result.Templates.FirstOrDefault(x => x.Name == MenuImageTemplate && x.Language == StarterLanguage);
        return string.Equals(image?.Status, "APPROVED", StringComparison.OrdinalIgnoreCase) ? image : standard ?? image;
    }

    private string? StarterUnavailableReason(Branch branch, string key,
        WhatsAppBranchTransferAccount? account, WhatsAppCloudTemplate? template)
    {
        if (template is null) return "Esta línea todavía no tiene esta plantilla en Meta.";
        if (!string.Equals(template.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            return string.Equals(template.Status, "PENDING", StringComparison.OrdinalIgnoreCase)
                ? "En revisión en Meta." : "Meta no habilita el envío de esta plantilla.";
        var parameters = CountBodyTemplateParameters(template.ComponentsJson);
        var validParameters = key switch
        {
            "payment" => parameters == 3,
            "delivery_update" => parameters == 1,
            _ => parameters is 0 or 1
        };
        if (!validParameters)
            return "Las variables aprobadas no coinciden con la configuración del POS.";
        if (!HasStarterComponents(template, key))
            return "Los componentes aprobados no coinciden con esta plantilla.";
        if (key == "menu" && (MenuImages(branch).Length == 0
            || (template.Name == MenuDocumentTemplate && _menuStorage is null)))
            return "Configura la carta en esta sucursal.";
        if (key == "payment" && account is null)
            return "Configura la cuenta Bancolombia de esta sucursal.";
        return null;
    }

    private static bool HasStarterComponents(WhatsAppCloudTemplate template, string key)
    {
        using var components = JsonDocument.Parse(template.ComponentsJson);
        var entries = components.RootElement.EnumerateArray().ToArray();
        if (key == "menu")
            return entries.Any(component => ComponentValue(component, "type") == "HEADER"
                && ComponentValue(component, "format") == (template.Name == MenuImageTemplate ? "IMAGE" : "DOCUMENT"));
        if (key == "catalog")
            return entries.Any(component => ComponentValue(component, "type") == "BUTTONS"
                && component.TryGetProperty("buttons", out var buttons) && buttons.ValueKind == JsonValueKind.Array
                && buttons.GetArrayLength() > 0 && ComponentValue(buttons[0], "type") == "CATALOG");
        return entries.All(component => ComponentValue(component, "type") is "BODY" or "FOOTER");
    }

    private static string? ComponentValue(JsonElement component, string name) =>
        component.ValueKind == JsonValueKind.Object && component.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString()?.ToUpperInvariant() : null;

    internal static string? NormalizeStarterPhone(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 40 || !Regex.IsMatch(input.Trim(), @"^\+?[0-9 ().-]+$"))
            return null;
        var digits = NormalizeWhatsAppPhone(input);
        if (!input.TrimStart().StartsWith('+') && digits.Length == 10 && digits.StartsWith('3'))
            return "57" + digits;
        if (digits.Length == 12 && digits.StartsWith("573", StringComparison.Ordinal)) return digits;
        return input.TrimStart().StartsWith('+') && IsInternationalWhatsAppPhone(digits) ? digits : null;
    }

    private static string[] MenuImages(Branch branch) =>
        new[] { branch.MenuImageUrl1, branch.MenuImageUrl2 }.OfType<string>().Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

    private async Task<(string? Id, string? Url)> PrepareStarterMenuAsync(ConversationStarter sender, string documentName, CancellationToken ct)
    {
        if (_menuStorage is null) return (null, null);
        try
        {
            var images = MenuImages(sender.Branch);
            using var pdf = new PdfDocument();
            foreach (var url in images)
            {
                var bytes = await _menuStorage.DownloadObjectFromUrlAsync(url, MaxMenuImageBytes, ct);
                if (bytes is not { Length: > 0 }) return (null, null);
                using var stream = new MemoryStream(bytes, 0, bytes.Length, false, true);
                using var image = XImage.FromStream(stream);
                var page = pdf.AddPage();
                var scale = Math.Min(595d / image.PixelWidth, 1200d / image.PixelHeight);
                page.Width = XUnit.FromPoint(image.PixelWidth * scale);
                page.Height = XUnit.FromPoint(image.PixelHeight * scale);
                using var canvas = XGraphics.FromPdfPage(page);
                canvas.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
            }
            using var output = new MemoryStream();
            pdf.Save(output, false);
            var content = output.ToArray();
            if (content.Length > 20_000_000) return (null, null);
            var publicUrl = await _menuStorage.UploadPublicObjectAsync(content,
                $"whatsapp/{TenantId}/menus/{sender.Branch.Id}/{Guid.NewGuid():N}.pdf", MenuDocumentType, ct);
            var media = await _whatsAppCloudClient.UploadMediaAsync(sender.PhoneNumberId, sender.AccessToken,
                content, documentName, MenuDocumentType, ct);
            return media.Success ? (media.MediaId, publicUrl) : (null, null);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _logger.LogWarning("No se pudo generar la carta PDF. BranchId={BranchId} ErrorType={ErrorType}",
                sender.Branch.Id, error.GetType().Name);
            return (null, null);
        }
    }

    private sealed record ConversationStarter(Branch Branch, int? ChannelId, int? BranchLineId,
        string PhoneNumberId, string BusinessAccountId, string AccessToken, string DisplayPhoneNumber);
}
