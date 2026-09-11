using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;

namespace SenorArroz.API.Services;

public sealed class StorefrontCustomerAuthService(
    IApplicationDbContext db,
    IWhatsAppCloudClient whatsApp,
    IClock clock,
    IOptions<StorefrontCustomerAuthOptions> options,
    ILogger<StorefrontCustomerAuthService> logger)
{
    private readonly StorefrontCustomerAuthOptions _options = options.Value;

    public async Task<StorefrontOtpRequestResult> RequestCodeAsync(string rawPhone, string requestIp, CancellationToken ct)
    {
        EnsureConfigured();
        var phone = NormalizePhone(rawPhone);
        var now = clock.UtcNow;
        var ipHash = HmacHex($"ip:{requestIp}");
        var hourAgo = now.AddHours(-1);

        var latestSentAt = await db.StorefrontCustomerAuthChallenges
            .AsNoTracking()
            .Where(x => x.TenantId == _options.TenantId && x.Phone == phone && x.SentAt != null)
            .OrderByDescending(x => x.SentAt)
            .Select(x => x.SentAt)
            .FirstOrDefaultAsync(ct);
        if (latestSentAt.HasValue && latestSentAt.Value.AddSeconds(_options.ResendSeconds) > now)
            throw new StorefrontAuthRateLimitException("Espera un momento antes de solicitar otro código.");

        var phoneSends = await db.StorefrontCustomerAuthChallenges.CountAsync(
            x => x.TenantId == _options.TenantId && x.Phone == phone && x.SentAt >= hourAgo, ct);
        var ipSends = await db.StorefrontCustomerAuthChallenges.CountAsync(
            x => x.TenantId == _options.TenantId && x.RequestIpHash == ipHash && x.SentAt >= hourAgo, ct);
        if (phoneSends >= _options.MaxSendsPerPhonePerHour || ipSends >= _options.MaxSendsPerIpPerHour)
            throw new StorefrontAuthRateLimitException("No fue posible enviar más códigos en este momento. Intenta más tarde.");

        var setting = await db.WhatsAppBranchSettings.AsNoTracking().FirstOrDefaultAsync(
            x => x.BranchId == _options.AuthenticationBranchId && x.IsActive && x.IsVerified, ct);
        if (setting is null)
            throw new StorefrontAuthUnavailableException("La verificación por WhatsApp no está disponible en este momento.");

        var previousChallenges = await db.StorefrontCustomerAuthChallenges
            .Where(x => x.TenantId == _options.TenantId && x.Phone == phone && x.ConsumedAt == null)
            .ToListAsync(ct);
        foreach (var previousChallenge in previousChallenges)
            previousChallenge.ConsumedAt = now;

        var publicId = Guid.NewGuid();
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var challenge = new StorefrontCustomerAuthChallenge
        {
            TenantId = _options.TenantId,
            PublicId = publicId,
            Phone = phone,
            CodeHmac = HmacHex($"otp:{publicId:N}:{phone}:{code}"),
            ExpiresAt = now.AddMinutes(_options.CodeLifetimeMinutes),
            ResendAvailableAt = now.AddSeconds(_options.ResendSeconds),
            MaxAttempts = _options.MaxAttempts,
            RequestIpHash = ipHash
        };
        db.StorefrontCustomerAuthChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);

        var send = await whatsApp.SendAuthenticationTemplateMessageAsync(
            setting.PhoneNumberId,
            setting.AccessToken,
            $"57{phone}",
            _options.TemplateName,
            _options.TemplateLanguage,
            code,
            ct);
        if (!send.Success)
        {
            challenge.ConsumedAt = now;
            await db.SaveChangesAsync(ct);
            logger.LogWarning("No se pudo enviar OTP del storefront para challenge {ChallengeId}: {Error}", publicId, send.ErrorMessage);
            throw new StorefrontAuthUnavailableException("No fue posible enviar el código por WhatsApp. Intenta nuevamente.");
        }

        challenge.SentAt = now;
        await db.SaveChangesAsync(ct);
        return new(publicId, _options.CodeLifetimeMinutes * 60, _options.ResendSeconds);
    }

    public async Task<StorefrontOtpVerificationResult> VerifyCodeAsync(Guid publicId, string rawCode, CancellationToken ct)
    {
        EnsureConfigured();
        var now = clock.UtcNow;
        var challenge = await db.StorefrontCustomerAuthChallenges.FirstOrDefaultAsync(
            x => x.TenantId == _options.TenantId && x.PublicId == publicId, ct);
        if (challenge is null || challenge.SentAt is null || challenge.ConsumedAt.HasValue || challenge.ExpiresAt <= now || challenge.AttemptCount >= challenge.MaxAttempts)
            throw new StorefrontAuthInvalidCodeException();

        var code = new string((rawCode ?? string.Empty).Where(char.IsDigit).ToArray());
        challenge.AttemptCount++;
        var candidate = HmacHex($"otp:{publicId:N}:{challenge.Phone}:{code}");
        if (code.Length != 6 || !FixedEqualsHex(challenge.CodeHmac, candidate))
        {
            if (challenge.AttemptCount >= challenge.MaxAttempts)
                challenge.ConsumedAt = now;
            await db.SaveChangesAsync(ct);
            throw new StorefrontAuthInvalidCodeException();
        }

        var sessionSecret = Base64Url(RandomNumberGenerator.GetBytes(32));
        challenge.ConsumedAt = now;
        challenge.SessionTokenHash = Sha256Hex(sessionSecret);
        challenge.SessionExpiresAt = now.AddDays(_options.SessionLifetimeDays);
        await db.SaveChangesAsync(ct);

        var customer = await ResolveCustomerAsync(challenge.Phone, ct);
        return new($"{publicId:N}.{sessionSecret}", _options.SessionLifetimeDays * 24 * 60 * 60, customer);
    }

    public async Task<StorefrontCustomerSessionResult> GetSessionAsync(string? token, CancellationToken ct)
    {
        EnsureConfigured();
        var parts = (token ?? string.Empty).Split('.', 2);
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var publicId) || string.IsNullOrWhiteSpace(parts[1]))
            throw new StorefrontAuthInvalidSessionException();

        var now = clock.UtcNow;
        var challenge = await db.StorefrontCustomerAuthChallenges.AsNoTracking().FirstOrDefaultAsync(
            x => x.TenantId == _options.TenantId && x.PublicId == publicId && x.SessionExpiresAt > now, ct);
        if (challenge?.SessionTokenHash is null || !FixedEqualsHex(challenge.SessionTokenHash, Sha256Hex(parts[1])))
            throw new StorefrontAuthInvalidSessionException();

        return await ResolveCustomerAsync(challenge.Phone, ct);
    }

    public Task<StorefrontCustomerSessionResult> ResolveTrustedPhoneAsync(string phone, CancellationToken ct) =>
        ResolveCustomerAsync(NormalizePhone(phone), ct);

    private async Task<StorefrontCustomerSessionResult> ResolveCustomerAsync(string phone, CancellationToken ct)
    {
        var matches = await db.Customers.AsNoTracking()
            .Include(x => x.Addresses)
            .ThenInclude(x => x.Neighborhood)
            .Where(x => x.Active && (x.Phone1 == phone || x.Phone2 == phone))
            .OrderBy(x => x.Id)
            .Take(2)
            .ToListAsync(ct);

        if (matches.Count == 0)
            return new(phone, false, false, null, []);
        if (matches.Count > 1)
            return new(phone, false, true, null, []);

        var customer = matches[0];
        var addresses = customer.Addresses
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.Id)
            .Select(x => new StorefrontCustomerAddressResult(
                x.Id,
                x.Label,
                x.AddressText,
                x.AdditionalInfo,
                x.DeliveryFee,
                x.Latitude,
                x.Longitude,
                x.IsPrimary,
                x.Neighborhood?.Name))
            .ToList();
        return new(phone, true, false, new(customer.Id, customer.Name), addresses);
    }

    private void EnsureConfigured()
    {
        if (_options.TenantId <= 0 || _options.AuthenticationBranchId <= 0 || Encoding.UTF8.GetByteCount(_options.HmacSecret) < 32)
            throw new StorefrontAuthUnavailableException("La autenticación del storefront no está configurada.");
    }

    private string HmacHex(string value)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.HmacSecret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static string NormalizePhone(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length > 10)
            digits = digits[^10..];
        if (digits.Length != 10 || digits[0] != '3')
            throw new StorefrontAuthInvalidPhoneException();
        return digits;
    }

    private static string Sha256Hex(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool FixedEqualsHex(string expected, string candidate) => CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(candidate));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record StorefrontOtpRequestResult(Guid ChallengeId, int ExpiresInSeconds, int ResendAfterSeconds);
public sealed record StorefrontOtpVerificationResult(string SessionToken, int SessionExpiresInSeconds, StorefrontCustomerSessionResult CustomerSession);
public sealed record StorefrontCustomerSessionResult(string Phone, bool ExistingCustomer, bool AmbiguousCustomer, StorefrontCustomerResult? Customer, IReadOnlyCollection<StorefrontCustomerAddressResult> Addresses);
public sealed record StorefrontCustomerResult(int Id, string Name);
public sealed record StorefrontCustomerAddressResult(int Id, string? Label, string Address, string? AdditionalInfo, int DeliveryFee, decimal? Latitude, decimal? Longitude, bool IsPrimary, string? NeighborhoodName);

public sealed class StorefrontAuthInvalidPhoneException : Exception;
public sealed class StorefrontAuthInvalidCodeException : Exception;
public sealed class StorefrontAuthInvalidSessionException : Exception;
public sealed class StorefrontAuthRateLimitException(string message) : Exception(message);
public sealed class StorefrontAuthUnavailableException(string message) : Exception(message);
