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

    public sealed record Match(int Id, int? BranchId, string Kind, string Name, string? DeviceInstallationId = null);

    public static async Task<Match?> FindAsync(IApplicationDbContext db, WhatsAppConversation conversation, CancellationToken ct)
    {
        var phone = Normalize(conversation.PhoneNumber);
        if (phone is null) return null;
        var matches = await FindManyAsync(db, conversation.TenantId, [phone], ct);
        return matches.GetValueOrDefault(phone);
    }

    public static async Task EnrichAsync(IApplicationDbContext db, int tenantId,
        IReadOnlyList<WhatsAppConversationDto> chats, CancellationToken ct, DateTime? utcNow = null)
    {
        var phones = chats.Select(x => Normalize(x.PhoneNumber)).OfType<string>().Distinct().ToArray();
        var matches = await ResolveAsync(db, tenantId, phones, utcNow ?? DateTime.UtcNow, ct);
        foreach (var chat in chats)
        {
            var phone = Normalize(chat.PhoneNumber);
            var match = phone is null ? null : matches.GetValueOrDefault(phone);
            chat.OperationalContact = match is null ? null : new(match.Kind, match.Name,
                match.DeviceInstallationId is not null, match is { Kind: Deliveryman, Id: > 0 } ? match.Id : null);
        }
    }

    public static async Task<Match?> FindForRoutesAsync(IApplicationDbContext db, WhatsAppConversation conversation,
        DateTime utcNow, CancellationToken ct)
    {
        var phone = Normalize(conversation.PhoneNumber);
        if (phone is null) return null;
        var matches = await ResolveAsync(db, conversation.TenantId, [phone], utcNow, ct);
        return matches.GetValueOrDefault(phone);
    }

    public static async Task<Dictionary<string, Match>> ResolveAsync(IApplicationDbContext db, int tenantId,
        string[] phones, DateTime utcNow, CancellationToken ct)
    {
        var matches = await FindManyAsync(db, tenantId, phones, ct);
        await ResolveSharedPhonesAsync(db, tenantId, matches, utcNow, ct);
        return matches;
    }

    public static IQueryable<WhatsAppConversation> WithPhones(IQueryable<WhatsAppConversation> conversations, string[] phones)
    {
        if (phones.Length == 0) return conversations.Where(_ => false);

        var variants = PhoneVariants(phones).Concat(phones.Select(p => "+" + p)).ToArray();
        return conversations.Where(c => c.PhoneNumber != null && (variants.Contains(c.PhoneNumber)
            || variants.Contains(c.PhoneNumber.Trim().Replace(" ", "").Replace("(", "")
                .Replace(")", "").Replace("-", "").Replace(".", ""))));
    }

    private static string[] PhoneVariants(string[] phones) => phones.Concat(phones
        .Where(p => p.Length == 12 && p.StartsWith("573", StringComparison.Ordinal)).Select(p => p[2..]))
        .Distinct(StringComparer.Ordinal).ToArray();

    private static async Task ResolveSharedPhonesAsync(IApplicationDbContext db, int tenantId,
        Dictionary<string, Match> matches, DateTime utcNow, CancellationToken ct)
    {
        var shared = matches.Where(x => x.Value.DeviceInstallationId is not null).ToArray();
        if (shared.Length == 0) return;

        var devices = shared.Select(x => x.Value.DeviceInstallationId!).ToArray();
        var branches = shared.Select(x => x.Value.BranchId!.Value).Distinct().ToArray();
        var latestIds = db.DeliveryWorkSessions
            .Where(s => s.TenantId == tenantId && branches.Contains(s.BranchId)
                && devices.Contains(s.DeviceInstallationId) && s.StartedAt <= utcNow)
            .GroupBy(s => new { s.BranchId, s.DeviceInstallationId })
            .Select(g => g.OrderByDescending(s => s.StartedAt).ThenByDescending(s => s.Id).Select(s => s.Id).First());
        var sessions = await db.DeliveryWorkSessions.AsNoTracking()
            .Where(s => s.TenantId == tenantId && latestIds.Contains(s.Id))
            .Select(s => new { s.BranchId, s.DeviceInstallationId, s.DeliverymanId, s.Deliveryman.Name, s.Deliveryman.Active })
            .ToListAsync(ct);
        var latest = sessions.ToDictionary(s => (s.BranchId, s.DeviceInstallationId));

        foreach (var (phone, contact) in shared)
        {
            if (latest.TryGetValue((contact.BranchId!.Value, contact.DeviceInstallationId!), out var session) && session.Active)
                matches[phone] = contact with { Id = session.DeliverymanId, Name = session.Name };
        }
    }

    private static async Task<Dictionary<string, Match>> FindManyAsync(IApplicationDbContext db, int tenantId,
        string[] phones, CancellationToken ct)
    {
        if (tenantId <= 0 || phones.Length == 0) return [];

        var phoneSet = phones.ToHashSet(StringComparer.Ordinal);
        var storedVariants = PhoneVariants(phones);

        var drivers = db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.Active && u.AccountType != EmployeeAccountType.Terminal
                && (u.Role == UserRole.Deliveryman || (u.DeliveryAppEnabled
                    && (u.Role == UserRole.Admin || u.Role == UserRole.Superadmin || u.Role == UserRole.Cashier)))
                && storedVariants.Contains(u.Phone.Replace("+", "").Replace(" ", "")
                    .Replace("(", "").Replace(")", "").Replace("-", "").Replace(".", "")))
            .Select(u => new { u.Id, BranchId = (int?)u.BranchId, u.Name, u.Phone, Kind = Deliveryman, Device = (string?)null });
        var suppliers = db.Suppliers.AsNoTracking()
            .Where(s => s.TenantId == tenantId && storedVariants.Contains(s.Phone.Replace("+", "").Replace(" ", "")
                .Replace("(", "").Replace(")", "").Replace("-", "").Replace(".", "")))
            .Select(s => new { s.Id, s.BranchId, s.Name, s.Phone, Kind = Supplier, Device = (string?)null });
        var sharedPhones = db.DeliveryWorkPhones.AsNoTracking()
            .Where(x => x.TenantId == tenantId && phones.Contains(x.Phone))
            .Select(x => new { Id = 0, BranchId = (int?)x.BranchId, Name = "", x.Phone, Kind = Deliveryman, Device = (string?)x.DeviceInstallationId });
        var contacts = await drivers.Concat(suppliers).Concat(sharedPhones).ToListAsync(ct);

        return contacts.Select(x => (Phone: Normalize(x.Phone), Contact: new Match(x.Id, x.BranchId, x.Kind, x.Name, x.Device)))
            .Where(x => x.Phone is not null && phoneSet.Contains(x.Phone))
            .GroupBy(x => x.Phone!, StringComparer.Ordinal)
            .Select(g => (Phone: g.Key, Contact: g.FirstOrDefault(x => x.Contact.DeviceInstallationId is not null).Contact
                ?? (g.Count() == 1 ? g.Single().Contact : null)))
            .Where(x => x.Contact is not null)
            .ToDictionary(x => x.Phone, x => x.Contact!, StringComparer.Ordinal);
    }

    public static string? Normalize(string? input)
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
