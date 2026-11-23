using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Helpers;

public static class WhatsAppOperationalContacts
{
    public const string Deliveryman = "deliveryman";
    public const string Supplier = "supplier";
    public const string ManualAttention = "operational_contact_manual_attention";

    public sealed record Match(int Id, int? BranchId, string Kind, string Name);

    public static async Task<Match?> FindAsync(IApplicationDbContext db, WhatsAppConversation conversation, CancellationToken ct)
    {
        var phone = Normalize(conversation.PhoneNumber);
        if (phone is null) return null;
        var matches = await FindManyAsync(db, conversation.TenantId, [phone], ct);
        return matches.GetValueOrDefault(phone);
    }

    public static async Task EnrichAsync(IApplicationDbContext db, int tenantId,
        IReadOnlyList<WhatsAppConversationDto> chats, CancellationToken ct)
    {
        var phones = chats.Select(x => Normalize(x.PhoneNumber)).OfType<string>().Distinct().ToArray();
        var matches = await FindManyAsync(db, tenantId, phones, ct);
        foreach (var chat in chats)
        {
            var phone = Normalize(chat.PhoneNumber);
            var match = phone is null ? null : matches.GetValueOrDefault(phone);
            chat.OperationalContact = match is null ? null : new(match.Kind, match.Name);
        }
    }

    private static async Task<Dictionary<string, Match>> FindManyAsync(IApplicationDbContext db, int tenantId,
        string[] phones, CancellationToken ct)
    {
        if (tenantId <= 0 || phones.Length == 0) return [];

        var phoneSet = phones.ToHashSet(StringComparer.Ordinal);
        var storedVariants = phones.Concat(phones.Where(p => p.Length == 12 && p.StartsWith("573"))
            .Select(p => p[2..])).Distinct().ToArray();

        var drivers = db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.Active && u.AccountType != EmployeeAccountType.Terminal
                && (u.Role == UserRole.Deliveryman || (u.DeliveryAppEnabled
                    && (u.Role == UserRole.Admin || u.Role == UserRole.Superadmin || u.Role == UserRole.Cashier)))
                && storedVariants.Contains(u.Phone.Replace("+", "").Replace(" ", "")
                    .Replace("(", "").Replace(")", "").Replace("-", "").Replace(".", "")))
            .Select(u => new { u.Id, BranchId = (int?)u.BranchId, u.Name, u.Phone, Kind = Deliveryman });
        var suppliers = db.Suppliers.AsNoTracking()
            .Where(s => s.TenantId == tenantId && storedVariants.Contains(s.Phone.Replace("+", "").Replace(" ", "")
                .Replace("(", "").Replace(")", "").Replace("-", "").Replace(".", "")))
            .Select(s => new { s.Id, s.BranchId, s.Name, s.Phone, Kind = Supplier });
        var contacts = await drivers.Concat(suppliers).ToListAsync(ct);

        return contacts.Select(x => (Phone: Normalize(x.Phone), Contact: new Match(x.Id, x.BranchId, x.Kind, x.Name)))
            .Where(x => x.Phone is not null && phoneSet.Contains(x.Phone))
            .GroupBy(x => x.Phone!, StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().Contact, StringComparer.Ordinal);
    }

    private static string? Normalize(string? input)
    {
        var value = input?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 40) return null;
        var international = value.StartsWith('+');
        if (international) value = value[1..];
        if (value.Any(c => c is not (>= '0' and <= '9' or ' ' or '-' or '(' or ')' or '.'))) return null;
        var digits = WhatsAppWebhookIdentityReader.NormalizePhone(value);
        if (digits is null || digits.Length is < 8 or > 15 || digits[0] == '0') return null;
        return !international && digits.Length == 10 && digits[0] == '3' ? $"57{digits}" : digits;
    }
}
