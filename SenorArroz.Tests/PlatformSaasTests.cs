using System.Security.Cryptography;
using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Saas.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Services;
using SenorArroz.Domain.Models;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Services;
using SenorArroz.API.Middleware;

namespace SenorArroz.Tests;

public sealed class PlatformSaasTests
{
    private static readonly PlatformRequestContext RequestContext = new("127.0.0.1", "tests", "test-correlation");

    [Fact]
    public async Task Disabled_addon_returns_403_before_controller_execution()
    {
        var nextCalled = false;
        var middleware = new TenantCapabilityMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var capabilities = new Mock<ITenantCapabilityService>();
        capabilities.Setup(x => x.HasAddonAsync("whatsapp_ai", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1")], "Bearer"));
        context.Request.Path = "/api/whatsapp/conversations";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, capabilities.Object);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task Otp_has_five_attempts_and_cannot_be_used_after_limit()
    {
        await using var db = CreateDb();
        var passwords = new TestPasswordService();
        db.PlatformUsers.Add(new PlatformUser { Id = 1, Name = "Santiago", Email = "santyvano@outlook.com", PasswordHash = passwords.HashPassword("clave") });
        await db.SaveChangesAsync();
        string sentCode = string.Empty;
        var auth = CreateAuth(db, passwords, code => sentCode = code);

        var login = await auth.LoginAsync(new PlatformLoginRequest("santyvano@outlook.com", "clave", "Equipo"), null, RequestContext);
        Assert.True(login.OtpRequired);
        Assert.InRange(login.ChallengeExpiresAt!.Value, DateTime.UtcNow.AddMinutes(9), DateTime.UtcNow.AddMinutes(10).AddSeconds(5));

        for (var attempt = 0; attempt < 5; attempt++)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => auth.VerifyOtpAsync(new PlatformVerifyOtpRequest(login.ChallengeId!.Value, sentCode == "000000" ? "999999" : "000000", "Equipo"), RequestContext));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => auth.VerifyOtpAsync(new PlatformVerifyOtpRequest(login.ChallengeId!.Value, sentCode, "Equipo"), RequestContext));
        Assert.Equal(5, (await db.PlatformOtpChallenges.SingleAsync()).AttemptCount);
    }

    [Fact]
    public async Task Valid_otp_creates_csrf_session_and_revocable_trusted_device()
    {
        await using var db = CreateDb();
        var passwords = new TestPasswordService();
        db.PlatformUsers.Add(new PlatformUser { Id = 1, Name = "Santiago", Email = "santyvano@outlook.com", PasswordHash = passwords.HashPassword("clave") });
        await db.SaveChangesAsync();
        string sentCode = string.Empty;
        var currentUser = new PlatformCurrentUser { Id = 1, IsAuthenticated = true };
        var auth = CreateAuth(db, passwords, code => sentCode = code, currentUser);

        var login = await auth.LoginAsync(new PlatformLoginRequest("santyvano@outlook.com", "clave", "Equipo"), null, RequestContext);
        var verified = await auth.VerifyOtpAsync(new PlatformVerifyOtpRequest(login.ChallengeId!.Value, sentCode, "Equipo"), RequestContext);

        Assert.NotNull(await auth.ValidateSessionAsync(verified.SessionToken, verified.CsrfToken, true));
        Assert.Null(await auth.ValidateSessionAsync(verified.SessionToken, "csrf-invalido", true));
        var trustedLogin = await auth.LoginAsync(new PlatformLoginRequest("santyvano@outlook.com", "clave", "Equipo"), verified.TrustedDeviceToken, RequestContext);
        Assert.False(trustedLogin.OtpRequired);
        var device = Assert.Single(await auth.GetTrustedDevicesAsync());

        await auth.RevokeTrustedDeviceAsync(device.PublicId, RequestContext);
        var loginAfterRevocation = await auth.LoginAsync(new PlatformLoginRequest("santyvano@outlook.com", "clave", "Equipo"), verified.TrustedDeviceToken, RequestContext);
        Assert.True(loginAfterRevocation.OtpRequired);
    }

    [Fact]
    public async Task Published_plan_version_is_immutable()
    {
        await using var db = CreateDb();
        db.SaasPlans.Add(new SaasPlan
        {
            Id = 1,
            Code = "esencial",
            Name = "Esencial",
            Versions = { new SaasPlanVersion { Id = 1, VersionNumber = 1, Status = PlanVersionStatus.Published } }
        });
        await db.SaveChangesAsync();
        var service = CreatePlatformService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdatePlanVersionAsync(1, new UpsertPlanVersionRequest
        {
            Currency = "COP",
            BranchLimit = 1,
            UserLimit = 10
        }, RequestContext));
    }

    [Fact]
    public async Task Invitation_is_single_use_and_expired_invitation_is_rejected()
    {
        await using var db = CreateDb();
        var passwords = new TestPasswordService();
        var tenant = new Tenant { Id = 1, DisplayName = "Cliente", Slug = "cliente", Status = TenantStatus.Draft };
        var branch = new Branch { Id = 1, TenantId = 1, Name = "Principal", Address = "DirecciÃ³n", Phone1 = "300" };
        var user = new User { Id = 1, TenantId = 1, BranchId = 1, Name = "Admin", Email = "admin@cliente.com", Phone = "300", PasswordHash = "temporal", Active = false, Role = UserRole.Superadmin };
        var validToken = "token-valido";
        var invitation = new TenantInvitation { Id = 1, TenantId = 1, BranchId = 1, UserId = 1, Email = user.Email, TokenHash = Hash(validToken), ExpiresAt = DateTime.UtcNow.AddHours(1), Tenant = tenant, Branch = branch, User = user };
        var expired = new TenantInvitation { Id = 2, TenantId = 1, BranchId = 1, UserId = 1, Email = user.Email, TokenHash = Hash("expirado"), ExpiresAt = DateTime.UtcNow.AddMinutes(-1), Tenant = tenant, Branch = branch, User = user };
        db.AddRange(tenant, branch, user, invitation, expired);
        await db.SaveChangesAsync();
        var service = CreatePlatformService(db, passwords);

        await service.AcceptInvitationAsync(new AcceptTenantInvitationRequest(invitation.PublicId, validToken, "nueva-clave"));

        Assert.True(user.Active);
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.True(passwords.VerifyPassword("nueva-clave", user.PasswordHash));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AcceptInvitationAsync(new AcceptTenantInvitationRequest(invitation.PublicId, validToken, "otra-clave")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AcceptInvitationAsync(new AcceptTenantInvitationRequest(expired.PublicId, "expirado", "otra-clave")));
    }

    [Fact]
    public async Task Suspending_tenant_revokes_refresh_tokens_and_live_connections()
    {
        await using var db = CreateDb();
        var plan = new SaasPlan { Id = 1, Code = "ilimitado", Name = "Ilimitado" };
        var version = new SaasPlanVersion { Id = 1, PlanId = 1, Plan = plan, VersionNumber = 1, Status = PlanVersionStatus.Published };
        var tenant = new Tenant { Id = 1, DisplayName = "Cliente", Slug = "cliente", Status = TenantStatus.Active };
        var branch = new Branch { Id = 1, TenantId = 1, Name = "Principal", Address = "DirecciÃ³n", Phone1 = "300" };
        var user = new User { Id = 1, TenantId = 1, BranchId = 1, Name = "Admin", Email = "admin@cliente.com", Phone = "300", PasswordHash = "hash", Role = UserRole.Superadmin };
        var refresh = new RefreshToken { Id = 1, TenantId = 1, UserId = 1, Token = "refresh", ExpiresAt = DateTime.UtcNow.AddDays(1) };
        db.AddRange(plan, version, tenant, branch, user, refresh, new TenantSubscription { TenantId = 1, PlanVersionId = 1, PlanVersion = version, Tenant = tenant, Status = TenantSubscriptionStatus.Active });
        await db.SaveChangesAsync();
        var connections = new TestConnectionRegistry();
        var service = CreatePlatformService(db, connections: connections);

        await service.ChangeTenantStatusAsync(1, new ChangeTenantStatusRequest("suspended", "mora"), RequestContext);

        Assert.True(refresh.IsRevoked);
        Assert.Equal(1, connections.RevokedTenantId);
        Assert.Equal(TenantStatus.Suspended, tenant.Status);
    }

    private static PlatformAuthService CreateAuth(
        ApplicationDbContext db,
        IPasswordService passwords,
        Action<string> captureCode,
        IPlatformCurrentUser? currentUser = null)
    {
        var email = new Mock<IEmailService>();
        email.Setup(x => x.SendPlatformOtpEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
            .Callback<string, string, string, DateTime>((_, _, code, _) => captureCode(code))
            .ReturnsAsync(EmailSendResult.Ok("test"));
        return new PlatformAuthService(db, passwords, email.Object, currentUser ?? new PlatformCurrentUser(), new TestExecutionContext());
    }

    private static PlatformService CreatePlatformService(
        ApplicationDbContext db,
        IPasswordService? passwords = null,
        ITenantConnectionRegistry? connections = null)
    {
        var email = new Mock<IEmailService>();
        email.Setup(x => x.SendTenantInvitationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
            .ReturnsAsync(EmailSendResult.Ok("test"));
        return new PlatformService(
            db,
            new TestExecutionContext(),
            new PlatformCurrentUser(),
            passwords ?? new TestPasswordService(),
            email.Object,
            connections ?? new TestConnectionRegistry(),
            new ConfigurationBuilder().AddInMemoryCollection().Build());
    }

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class TestPasswordService : IPasswordService
    {
        public string HashPassword(string password) => $"hash:{password}";
        public bool VerifyPassword(string password, string hash) => hash == HashPassword(password);
    }

    private sealed class TestExecutionContext : ITenantExecutionContext
    {
        public IDisposable BeginTenantScope(int tenantId, Guid? publicId = null) => new TestScope();
        public IDisposable BeginSystemScope() => new TestScope();
        private sealed class TestScope : IDisposable { public void Dispose() { } }
    }

    private sealed class TestConnectionRegistry : ITenantConnectionRegistry
    {
        public int? RevokedTenantId { get; private set; }
        public IDisposable Register(int tenantId, string connectionId, Action abort) => new TestScope();
        public void Revoke(int tenantId) => RevokedTenantId = tenantId;
        private sealed class TestScope : IDisposable { public void Dispose() { } }
    }
}
