using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Auth.Commands;
using SenorArroz.Application.Features.Auth.DTOs;
using SenorArroz.Application.Features.Users.Commands;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Services;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;
using SenorArroz.Infrastructure.Services;
using Testcontainers.PostgreSql;

namespace SenorArroz.Tests;

public sealed class AdminApplicationSessionTests
{
    [PostgreSqlIntegrationFact]
    public async Task ConcurrentPostgreSqlLogins_KeepOnlyLatestSessionInEachApplication()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using var f = await Fixture.Create(UserRole.Superadmin, options);
        await f.Db.Database.ExecuteSqlRawAsync("ALTER TABLE \"user\" DROP COLUMN active_delivery_session_id");
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Scripts", "add_admin_delivery_sessions.sql"));
        await f.Db.Database.ExecuteSqlRawAsync(script);
        await f.Db.Database.ExecuteSqlRawAsync(script);
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async index =>
        {
            await using var db = new ApplicationDbContext(options,
                currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
            return (Mobile: index % 2 == 0, Auth: await f.Login(index % 2 == 0, db));
        }));
        f.Db.ChangeTracker.Clear();
        var currentMobile = new List<AuthResponseDto>();
        var currentWeb = new List<AuthResponseDto>();
        foreach (var result in results)
        {
            if (await f.IsCurrent(result.Auth))
                (result.Mobile ? currentMobile : currentWeb).Add(result.Auth);
            else
                await Assert.ThrowsAsync<SessionReplacedException>(() => f.Refresh(result.Auth, result.Mobile));
        }
        var mobile = Assert.Single(currentMobile);
        var web = Assert.Single(currentWeb);
        Assert.True(await f.IsCurrent(await f.Refresh(mobile, true)));
        Assert.True(await f.IsCurrent(await f.Refresh(web, false)));
        await f.Auth.EndSessionIfCurrentAsync(1, Guid.NewGuid());
        Assert.True(await f.IsCurrent(mobile));
        Assert.True(await f.IsCurrent(web));
    }

    [Theory]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.Admin, false)]
    [InlineData(UserRole.Superadmin, true)]
    [InlineData(UserRole.Superadmin, false)]
    public async Task LoginAndRefresh_KeepOneIndependentSessionPerApplication(UserRole role, bool mobileFirst)
    {
        await using var f = await Fixture.Create(role);
        var first = await f.Login(mobileFirst);
        await f.AddWorkSession();
        var second = await f.Login(!mobileFirst);
        var mobile = mobileFirst ? first : second;
        var web = mobileFirst ? second : first;

        Assert.True(await f.IsCurrent(mobile));
        Assert.True(await f.IsCurrent(web));
        Assert.NotEqual(f.Jwt.GetSessionIdFromExpiredToken(mobile.Token), f.Jwt.GetSessionIdFromExpiredToken(web.Token));
        if (mobileFirst)
        {
            Assert.Equal(DeliveryWorkSessionStatus.Active, (await f.Db.DeliveryWorkSessions.SingleAsync()).Status);
            Assert.Single(f.Db.UserDeviceTokens);
        }
        Assert.Equal("Deliveryman", mobile.User.Role);
        Assert.Equal(role.ToString(), web.User.Role);
        Assert.False(await f.Auth.IsSessionCurrentAsync(1, f.Jwt.GetSessionIdFromExpiredToken(mobile.Token), role.ToString()));
        Assert.False(await f.Auth.IsSessionCurrentAsync(1, f.Jwt.GetSessionIdFromExpiredToken(web.Token), "Deliveryman"));

        var mobileRefreshed = await f.Refresh(mobile, true);
        var webRefreshed = await f.Refresh(web, false);
        Assert.True(await f.IsCurrent(mobileRefreshed));
        Assert.True(await f.IsCurrent(webRefreshed));
        Assert.Equal(f.Jwt.GetSessionIdFromExpiredToken(mobile.Token), f.Jwt.GetSessionIdFromExpiredToken(mobileRefreshed.Token));

        var replacement = await f.Login(mobileFirst);
        Assert.False(await f.IsCurrent(first));
        Assert.True(await f.IsCurrent(second));
        Assert.True(await f.IsCurrent(replacement));
        await Assert.ThrowsAsync<SessionReplacedException>(() => f.Refresh(
            mobileFirst ? mobileRefreshed : webRefreshed, mobileFirst));
        Assert.True(await f.IsCurrent(await f.Refresh(mobileFirst ? webRefreshed : mobileRefreshed, !mobileFirst)));
    }

    [Theory]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Deliveryman)]
    public async Task OtherRoles_KeepGlobalExclusivity(UserRole role)
    {
        await using var f = await Fixture.Create(role);
        var mobile = await f.Login(true);
        var web = await f.Login(false);
        Assert.False(await f.IsCurrent(mobile));
        Assert.True(await f.IsCurrent(web));
        await Assert.ThrowsAsync<SessionReplacedException>(() => f.Refresh(mobile, true));
        Assert.Null((await f.Db.Users.SingleAsync()).ActiveDeliverySessionId);
    }

    [Theory]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.Admin, false)]
    [InlineData(UserRole.Superadmin, true)]
    [InlineData(UserRole.Superadmin, false)]
    public async Task Logout_OnlyEndsItsApplicationAndWebKeepsPendingDelivery(UserRole role, bool logoutMobile)
    {
        await using var f = await Fixture.Create(role);
        var mobile = await f.Login(true);
        var web = await f.Login(false);
        await f.AddWorkSession();
        f.Db.Orders.Add(new Order { Id = 1, BranchId = 7, TakenById = 1, DeliveryManId = 1,
            Type = OrderType.Delivery, Status = logoutMobile ? OrderStatus.Delivered : OrderStatus.OnTheWay });
        await f.Db.SaveChangesAsync();

        Assert.True(await f.Logout(logoutMobile ? mobile : web));
        Assert.False(await f.IsCurrent(logoutMobile ? mobile : web));
        Assert.True(await f.IsCurrent(logoutMobile ? web : mobile));
        Assert.Equal(logoutMobile ? DeliveryWorkSessionStatus.Closed : DeliveryWorkSessionStatus.Active,
            (await f.Db.DeliveryWorkSessions.SingleAsync()).Status);
        if (!logoutMobile) Assert.Single(f.Db.UserDeviceTokens);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Superadmin)]
    public async Task MobileLogout_RejectsPendingDeliveriesWithoutEndingEitherSession(UserRole role)
    {
        await using var f = await Fixture.Create(role);
        var web = await f.Login(false);
        var mobile = await f.Login(true);
        await f.AddWorkSession();
        f.Db.Orders.Add(new Order { Id = 1, BranchId = 7, TakenById = 1, DeliveryManId = 1,
            Type = OrderType.Delivery, Status = OrderStatus.OnTheWay });
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<BusinessException>(() => f.Logout(mobile));
        Assert.True(await f.IsCurrent(web));
        Assert.True(await f.IsCurrent(mobile));
        Assert.Equal(DeliveryWorkSessionStatus.Active, (await f.Db.DeliveryWorkSessions.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Superadmin)]
    public async Task ReplacedLogoutAndCrossApplicationTokens_CannotCloseCurrentSessions(UserRole role)
    {
        await using var f = await Fixture.Create(role);
        var oldMobile = await f.Login(true);
        var web = await f.Login(false);
        var mobile = await f.Login(true);
        await f.AddWorkSession();

        Assert.True(await f.Logout(oldMobile));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Logout(web, mobile.RefreshToken));
        await Assert.ThrowsAsync<SessionReplacedException>(() => f.Refresh(mobile, false));
        Assert.Equal(role.ToString(), (await f.Refresh(web, true)).User.Role);
        await Assert.ThrowsAsync<SessionReplacedException>(() => f.Refresh(withRefresh(mobile.RefreshToken), false));
        Assert.True(await f.IsCurrent(web));
        Assert.True(await f.IsCurrent(mobile));
        Assert.Equal(DeliveryWorkSessionStatus.Active, (await f.Db.DeliveryWorkSessions.SingleAsync()).Status);

        AuthResponseDto withRefresh(string token) => new() { Token = web.Token, RefreshToken = token };
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Superadmin)]
    public async Task Reactivation_CannotRestoreEitherOldSession(UserRole role)
    {
        await using var f = await Fixture.Create(role);
        var web = await f.Login(false);
        var mobile = await f.Login(true);
        var users = new UserRepository(f.Db);
        var toggle = new ToggleStatusHandler(users, Mock.Of<IMapper>());
        await toggle.Handle(new ToggleStatusCommand(1), default);
        await toggle.Handle(new ToggleStatusCommand(1), default);
        Assert.False(await f.IsCurrent(web));
        Assert.False(await f.IsCurrent(mobile));
        web = await f.Login(false);
        mobile = await f.Login(true);
        await users.DeleteAsync(1);
        await toggle.Handle(new ToggleStatusCommand(1), default);
        Assert.False(await f.IsCurrent(web));
        Assert.False(await f.IsCurrent(mobile));
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Superadmin)]
    public async Task DeliveryOperations_ValidateMobileSessionAndCurrentPermissions(UserRole role)
    {
        await using var f = await Fixture.Create(role);
        var web = await f.Login(false);
        var mobile = await f.Login(true);
        var actor = f.Actor(mobile);
        await DeliveryRouteScope.EnsureCurrentSessionAsync(f.Db, actor, default);
        await f.Operations(actor).ContextAsync(default);
        var owner = await f.Db.Users.SingleAsync();
        owner.DeliveryAppEnabled = false;
        await f.Db.SaveChangesAsync();
        Assert.False(await f.IsCurrent(mobile));
        Assert.True(await f.IsCurrent(web));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Operations(actor).ContextAsync(default));
        owner.DeliveryAppEnabled = true;
        await f.Db.SaveChangesAsync();
        await f.Login(true);
        await Assert.ThrowsAsync<SessionReplacedException>(() => DeliveryRouteScope.EnsureCurrentSessionAsync(f.Db, actor, default));
        await Assert.ThrowsAsync<SessionReplacedException>(() => f.Operations(actor).ContextAsync(default));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; }
        public AuthRepository Auth { get; }
        public JwtService Jwt { get; }
        private readonly RefreshTokenRepository _refresh;
        private readonly IMapper _mapper;
        private readonly FakeClock _clock = new(DateTime.UtcNow);
        private readonly IDeliveryAppVersionPolicy _policy = Mock.Of<IDeliveryAppVersionPolicy>();

        private Fixture(UserRole role, DbContextOptions<ApplicationDbContext>? options = null)
        {
            Db = new ApplicationDbContext(options ?? new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
                currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
            var password = new Mock<IPasswordService>();
            password.Setup(x => x.VerifyPassword("secret", "hash")).Returns(true);
            Auth = new AuthRepository(Db, password.Object);
            _refresh = new RefreshTokenRepository(Db, _clock);
            Jwt = new JwtService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = new string('s', 64),
                ["JwtSettings:Issuer"] = "session-tests", ["JwtSettings:Audience"] = "session-tests",
            }).Build(), _clock);
            var mapper = new Mock<IMapper>();
            mapper.Setup(x => x.Map<UserInfoDto>(It.IsAny<User>())).Returns((User u) =>
                new UserInfoDto { Id = u.Id, Role = u.Role.ToString()! });
            _mapper = mapper.Object;
            var tenant = new Tenant { Id = 1, PublicId = Guid.NewGuid(), Name = "Test", Slug = "test",
                IsActive = true, Status = TenantStatus.Active, AccessVersion = 1 };
            var branch = new Branch { Id = 7, TenantId = 1, Tenant = tenant, Name = "Centro", Address = "Centro" };
            Db.Users.Add(new User { Id = 1, TenantId = 1, Tenant = tenant, BranchId = 7, Branch = branch,
                Role = role, DeliveryAppEnabled = true, WebAccessEnabled = true, Name = "Test",
                Email = "test@example.com", Phone = "3000000000", PasswordHash = "hash" });
        }

        public static async Task<Fixture> Create(UserRole role, DbContextOptions<ApplicationDbContext>? options = null)
        {
            var f = new Fixture(role, options);
            if (options is not null) await f.Db.Database.EnsureCreatedAsync();
            await f.Db.SaveChangesAsync();
            return f;
        }

        public Task<AuthResponseDto> Login(bool mobile, ApplicationDbContext? db = null) => new LoginHandler(
            db is null ? Auth : new AuthRepository(db, PasswordService()),
            db is null ? _refresh : new RefreshTokenRepository(db, _clock), Jwt, _mapper, _clock, db ?? Db, _policy)
            .Handle(new LoginCommand { Email = "test@example.com", Password = "secret", IsWebClient = !mobile,
                DeviceInstallationId = mobile ? Guid.NewGuid().ToString() : null,
                DeliveryAppVersion = mobile ? new DeliveryAppClientVersion("1.2.5", 11, "com.senorarroz.delivery_app") : null }, default);

        private static IPasswordService PasswordService()
        {
            var password = new Mock<IPasswordService>();
            password.Setup(x => x.VerifyPassword("secret", "hash")).Returns(true);
            return password.Object;
        }

        public Task<AuthResponseDto> Refresh(AuthResponseDto auth, bool mobile) =>
            new RefreshTokenHandler(Auth, _refresh, Jwt, _mapper, _clock, _policy).Handle(new RefreshTokenCommand
            { Token = auth.Token, RefreshToken = auth.RefreshToken, IsWebClient = !mobile }, default);

        public Task<bool> IsCurrent(AuthResponseDto auth) => Auth.IsSessionCurrentAsync(1,
            Jwt.GetSessionIdFromExpiredToken(auth.Token), Jwt.GetRoleFromExpiredToken(auth.Token)!);

        public ICurrentUser Actor(AuthResponseDto auth)
        {
            var actor = new Mock<ICurrentUser>();
            actor.SetupGet(x => x.Id).Returns(1);
            actor.SetupGet(x => x.TenantId).Returns(1);
            actor.SetupGet(x => x.BranchId).Returns(7);
            actor.SetupGet(x => x.Role).Returns(Jwt.GetRoleFromExpiredToken(auth.Token)!);
            actor.SetupGet(x => x.SessionId).Returns(Jwt.GetSessionIdFromExpiredToken(auth.Token));
            return actor.Object;
        }

        public DeliveryOperationsService Operations(ICurrentUser actor)
        {
            var routeLock = new Mock<IDeliveryAutoCompletionRouteLock>();
            routeLock.Setup(x => x.ExecuteDriverAsync(It.IsAny<int>(), It.IsAny<Func<CancellationToken, Task>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((int _, Func<CancellationToken, Task> action, CancellationToken ct) => action(ct));
            return new(Db, actor, _clock, Mock.Of<ISender>(), _mapper, routeLock.Object);
        }

        public Task<bool> Logout(AuthResponseDto auth, string? refreshToken = null)
        {
            var actor = Actor(auth);
            return new LogoutHandler(_refresh, Auth, _clock, Db, Operations(actor), actor)
                .Handle(new LogoutCommand { RefreshToken = refreshToken ?? auth.RefreshToken }, default);
        }

        public async Task AddWorkSession()
        {
            Db.DeliveryWorkSessions.Add(new DeliveryWorkSession { DeliverymanId = 1, BranchId = 7,
                DeviceInstallationId = "device", StartedAt = _clock.UtcNow, AutoCloseAt = _clock.UtcNow.AddHours(8),
                LastCommunicationAt = _clock.UtcNow, Status = DeliveryWorkSessionStatus.Active });
            Db.UserDeviceTokens.Add(new UserDeviceToken { UserId = 1, Token = "fcm" });
            await Db.SaveChangesAsync();
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
