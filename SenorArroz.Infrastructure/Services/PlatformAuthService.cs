using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Saas.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Services;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Services;

public sealed class PlatformAuthService : IPlatformAuthService
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordService _passwords;
    private readonly IEmailService _email;
    private readonly IPlatformCurrentUser _currentUser;
    private readonly ITenantExecutionContext _executionContext;

    public PlatformAuthService(
        ApplicationDbContext context,
        IPasswordService passwords,
        IEmailService email,
        IPlatformCurrentUser currentUser,
        ITenantExecutionContext executionContext)
    {
        _context = context;
        _passwords = passwords;
        _email = email;
        _currentUser = currentUser;
        _executionContext = executionContext;
    }

    public async Task<PlatformLoginResult> LoginAsync(PlatformLoginRequest request, string? trustedDeviceToken, PlatformRequestContext requestContext, CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _context.PlatformUsers.SingleOrDefaultAsync(x => x.Email == email && x.Active, cancellationToken);
        if (user is null || !_passwords.VerifyPassword(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Credenciales inválidas.");

        if (!string.IsNullOrWhiteSpace(trustedDeviceToken))
        {
            var tokenHash = Hash(trustedDeviceToken);
            var device = await _context.PlatformTrustedDevices.SingleOrDefaultAsync(
                x => x.PlatformUserId == user.Id && x.TokenHash == tokenHash && x.RevokedAt == null && x.ExpiresAt > DateTime.UtcNow,
                cancellationToken);
            if (device is not null)
            {
                device.LastUsedAt = DateTime.UtcNow;
                device.IpAddress = requestContext.IpAddress;
                var result = await CreateSessionAsync(user, requestContext, cancellationToken, false);
                Audit(user.Id, user.Email, "platform.auth.login", nameof(PlatformSession), "new", null, new { TrustedDevice = true }, requestContext);
                await _context.SaveChangesAsync(cancellationToken);
                return result;
            }
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var challenge = new PlatformOtpChallenge
        {
            PlatformUserId = user.Id,
            CodeHash = _passwords.HashPassword(code),
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            IpAddress = requestContext.IpAddress,
            UserAgent = requestContext.UserAgent,
            CreatedAt = DateTime.UtcNow
        };
        _context.PlatformOtpChallenges.Add(challenge);
        Audit(user.Id, user.Email, "platform.auth.otp_requested", nameof(PlatformOtpChallenge), challenge.PublicId.ToString(), null,
            new { challenge.ExpiresAt, challenge.MaxAttempts, request.DeviceName }, requestContext);
        await _context.SaveChangesAsync(cancellationToken);
        var emailResult = await _email.SendPlatformOtpEmailAsync(user.Email, user.Name, code, challenge.ExpiresAt);
        if (!emailResult.Success) throw new InvalidOperationException("No fue posible enviar el código de acceso.");

        return new PlatformLoginResult { OtpRequired = true, ChallengeId = challenge.PublicId, ChallengeExpiresAt = challenge.ExpiresAt };
    }

    public async Task<PlatformLoginResult> VerifyOtpAsync(PlatformVerifyOtpRequest request, PlatformRequestContext requestContext, CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        var challenge = await _context.PlatformOtpChallenges.Include(x => x.PlatformUser)
            .SingleOrDefaultAsync(x => x.PublicId == request.ChallengeId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Código inválido.");

        if (challenge.ConsumedAt is not null || challenge.ExpiresAt <= DateTime.UtcNow || challenge.AttemptCount >= challenge.MaxAttempts)
            throw new UnauthorizedAccessException("El código expiró o agotó sus intentos.");

        challenge.AttemptCount++;
        if (!_passwords.VerifyPassword(request.Code, challenge.CodeHash))
        {
            Audit(challenge.PlatformUserId, challenge.PlatformUser.Email, "platform.auth.otp_failed", nameof(PlatformOtpChallenge), challenge.PublicId.ToString(), null,
                new { challenge.AttemptCount, challenge.MaxAttempts }, requestContext);
            await _context.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedAccessException("Código inválido.");
        }

        challenge.ConsumedAt = DateTime.UtcNow;
        var trustedToken = GenerateToken();
        _context.PlatformTrustedDevices.Add(new PlatformTrustedDevice
        {
            PlatformUserId = challenge.PlatformUserId,
            TokenHash = Hash(trustedToken),
            Name = string.IsNullOrWhiteSpace(request.DeviceName) ? "Dispositivo" : request.DeviceName.Trim(),
            UserAgent = requestContext.UserAgent,
            IpAddress = requestContext.IpAddress,
            CreatedAt = DateTime.UtcNow,
            LastUsedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        });

        var result = await CreateSessionAsync(challenge.PlatformUser, requestContext, cancellationToken, false);
        Audit(challenge.PlatformUserId, challenge.PlatformUser.Email, "platform.auth.otp_verified", nameof(PlatformOtpChallenge), challenge.PublicId.ToString(), null,
            new { DeviceName = request.DeviceName, TrustedUntil = DateTime.UtcNow.AddDays(30) }, requestContext);
        await _context.SaveChangesAsync(cancellationToken);
        return new PlatformLoginResult
        {
            User = result.User,
            SessionToken = result.SessionToken,
            CsrfToken = result.CsrfToken,
            TrustedDeviceToken = trustedToken
        };
    }

    public async Task<PlatformSessionDto?> ValidateSessionAsync(string? sessionToken, string? csrfToken, bool requireCsrf, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionToken)) return null;
        using var scope = _executionContext.BeginSystemScope();
        var session = await _context.PlatformSessions.Include(x => x.PlatformUser).AsNoTracking()
            .SingleOrDefaultAsync(x => x.TokenHash == Hash(sessionToken) && x.RevokedAt == null && x.ExpiresAt > DateTime.UtcNow && x.PlatformUser.Active, cancellationToken);
        if (session is null || requireCsrf && (string.IsNullOrWhiteSpace(csrfToken) || !FixedEquals(session.CsrfTokenHash, Hash(csrfToken))))
            return null;
        return new PlatformSessionDto(session.PlatformUser.Id, session.PlatformUser.Name, session.PlatformUser.Email);
    }

    public async Task LogoutAsync(string? sessionToken, PlatformRequestContext requestContext, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionToken)) return;
        using var scope = _executionContext.BeginSystemScope();
        var session = await _context.PlatformSessions.SingleOrDefaultAsync(x => x.TokenHash == Hash(sessionToken), cancellationToken);
        if (session is null || session.RevokedAt is not null) return;
        session.RevokedAt = DateTime.UtcNow;
        var user = await _context.PlatformUsers.AsNoTracking().SingleAsync(x => x.Id == session.PlatformUserId, cancellationToken);
        Audit(user.Id, user.Email, "platform.auth.logout", nameof(PlatformSession), session.Id.ToString(), null, new { session.RevokedAt }, requestContext);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlatformTrustedDeviceDto>> GetTrustedDevicesAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        return await _context.PlatformTrustedDevices.AsNoTracking()
            .Where(x => x.PlatformUserId == _currentUser.Id && x.RevokedAt == null && x.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(x => x.LastUsedAt)
            .Select(x => new PlatformTrustedDeviceDto(x.PublicId, x.Name, x.UserAgent, x.IpAddress, x.LastUsedAt, x.ExpiresAt))
            .ToListAsync(cancellationToken);
    }

    public async Task RevokeTrustedDeviceAsync(Guid publicId, PlatformRequestContext requestContext, CancellationToken cancellationToken = default)
    {
        using var scope = _executionContext.BeginSystemScope();
        var device = await _context.PlatformTrustedDevices.SingleOrDefaultAsync(x => x.PublicId == publicId && x.PlatformUserId == _currentUser.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Dispositivo no encontrado.");
        device.RevokedAt = DateTime.UtcNow;
        Audit(_currentUser.Id, _currentUser.Email, "platform.auth.device_revoked", nameof(PlatformTrustedDevice), device.PublicId.ToString(), null,
            new { device.Name, device.RevokedAt }, requestContext);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<PlatformLoginResult> CreateSessionAsync(PlatformUser user, PlatformRequestContext requestContext, CancellationToken cancellationToken, bool save = true)
    {
        var rawSession = GenerateToken();
        var rawCsrf = GenerateToken();
        _context.PlatformSessions.Add(new PlatformSession
        {
            PlatformUserId = user.Id,
            TokenHash = Hash(rawSession),
            CsrfTokenHash = Hash(rawCsrf),
            IpAddress = requestContext.IpAddress,
            UserAgent = requestContext.UserAgent,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(12)
        });
        if (save) await _context.SaveChangesAsync(cancellationToken);
        return new PlatformLoginResult
        {
            User = new PlatformSessionDto(user.Id, user.Name, user.Email),
            SessionToken = rawSession,
            CsrfToken = rawCsrf
        };
    }

    private static string GenerateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool FixedEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));

    private void Audit(int platformUserId, string actor, string action, string entityType, string entityId, object? before, object? after, PlatformRequestContext context)
    {
        _context.PlatformAuditLogs.Add(new PlatformAuditLog
        {
            PlatformUserId = platformUserId,
            ActorIdentifier = actor,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            BeforeJson = PlatformAuditSerializer.Serialize(before),
            AfterJson = PlatformAuditSerializer.Serialize(after),
            IpAddress = context.IpAddress,
            CorrelationId = context.CorrelationId,
            CreatedAt = DateTime.UtcNow
        });
    }
}
