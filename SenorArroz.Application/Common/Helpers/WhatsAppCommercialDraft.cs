using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Helpers;

public static class WhatsAppCommercialDraft
{
    public const string AddressChoicePrefix = "order-address:";
    public const string ProductChoicePrefix = "order-product:";
    public const string ProductAnswerPrefix = "chosen-order-product:";
    public const string AddressApplied = "order_address_applied";
    public const string ProductApplied = "order_product_applied";
    public static readonly JsonSerializerOptions JsonOptions = Options();

    public sealed record ChoicePrompt(Guid RequestId, int SessionId, long AttentionRevision, int? CustomerId,
        int BranchId, OrderType OrderType, int? PreviousAddressId, int[]? AddressIds = null,
        int? ProductGroupId = null, Dictionary<int, int>? ProductPrices = null);

    public static string? ChoicePrefix(string? id) =>
        id?.StartsWith(AddressChoicePrefix, StringComparison.Ordinal) == true ? AddressChoicePrefix
        : id?.StartsWith(ProductChoicePrefix, StringComparison.Ordinal) == true ? ProductChoicePrefix : null;

    public static string ChoiceScope(string prefix, int? groupId) => groupId.HasValue ? $"{prefix}{groupId}:" : prefix;

    public static bool IsLegacyAdvisorConfirmation(WhatsAppCommerceOutboxMessage message) =>
        message.EventKey.StartsWith("whatsapp-order-created:", StringComparison.Ordinal)
        && message.Body.StartsWith("Pedido #", StringComparison.Ordinal)
        && message.Body.EndsWith("Seguimos atendiéndote por este chat.", StringComparison.Ordinal);

    public static IQueryable<Product> ChoiceCatalog(IApplicationDbContext db) => db.Products.AsNoTracking()
        .Where(x => x.CommercialProfileId != null && (x.Category.StorefrontRole == "rice" || x.Category.StorefrontRole == "addition"));

    public static string? ProductChoiceShortcut(string name, bool isRice)
    {
        var key = string.Concat(name.ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)));
        if (!isRice)
        {
            if (key.StartsWith("papas", StringComparison.Ordinal)) return "papas";
            if (key is "yuca" or "yucas") return "yucas";
            return null;
        }
        if (key.StartsWith("arroz", StringComparison.Ordinal)) key = key[5..];
        return key switch
        {
            "paisa" => "paisasin",
            "paisaconchicharron" or "paisachich" => "paisacon",
            "ropavieja" => "ropaviejasin",
            "ropaviejaconchicharron" or "ropaviejachich" => "ropaviejacon",
            { Length: > 0 and <= 40 } => key,
            _ => null
        };
    }

    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public static CreateOrderDto Read(WhatsAppCommerceSession session) =>
        JsonSerializer.Deserialize<CreateOrderDto>(session.DraftJson, JsonOptions) ?? new();

    public static int InitialBranch(WhatsAppConversation conversation) =>
        conversation.ChannelSettingId.HasValue ? conversation.OperationalBranchId.GetValueOrDefault() : conversation.BranchId;

    public static CreateOrderDto Read(WhatsAppCommerceSession session, WhatsAppConversation conversation)
    {
        var order = Read(session);
        if (order.BranchId <= 0 && session.Status == "active" && session.DraftSource == "advisor")
            order.BranchId = InitialBranch(conversation);
        return order;
    }

    public static void Write(WhatsAppCommerceSession session, CreateOrderDto draft)
    {
        draft.WhatsAppConversationId = session.ConversationId;
        draft.PosCreationKey = session.CorrelationId.ToString("D");
        session.DraftJson = JsonSerializer.Serialize(draft, JsonOptions);
        session.BranchId = draft.BranchId > 0 ? draft.BranchId : null;
        session.CustomerId = draft.CustomerId;
    }

    public static async Task LockAsync(IApplicationDbContext db, int tenantId, int conversationId, CancellationToken ct)
    {
        if (db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL" && db.Database.CurrentTransaction is not null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({tenantId}, {conversationId})", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM whatsapp_conversation WHERE tenant_id = {tenantId} AND id = {conversationId} FOR UPDATE", ct);
        }
    }

    public static WhatsAppCommerceSession New(WhatsAppConversation conversation, DateTime now) => new()
    {
        TenantId = conversation.TenantId,
        ConversationId = conversation.Id,
        Conversation = conversation,
        ChannelSettingId = conversation.ChannelSettingId,
        CustomerId = conversation.CustomerId,
        FlowTokenHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
        IdempotencyKey = $"wac_{Guid.NewGuid():N}",
        ExpiresAt = now.AddHours(24),
        DraftSource = "advisor"
    };
}
