using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Auth.Commands;
using SenorArroz.Application.Features.Auth.DTOs;
using SenorArroz.Application.Features.Deliverymen.Commands;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Interfaces.Services;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

public class ExclusiveDeliverySessionTests
{
    [Fact]
    public async Task Login_Deliveryman_ReplacesSessionAndClosesPreviousWorkSession()
    {
        await using var db = CreateDb();
        var now = new DateTime(2026, 7, 23, 15, 0, 0, DateTimeKind.Utc);
        var branch = new Branch { Id = 7, Name = "Centro", Address = "Sucursal" };
        db.Branches.Add(branch);
        db.Users.Add(new User
        {
            Id = 1,
            BranchId = 7,
            Role = UserRole.Deliveryman,
            Name = "Domiciliario",
            Email = "domiciliario@example.com",
            Phone = "3000000000",
            PasswordHash = "hash",
            Active = true,
        });
        db.DeliveryWorkSessions.Add(new DeliveryWorkSession
        {
            Id = 10,
            DeliverymanId = 1,
            BranchId = 7,
            DeviceInstallationId = "device-a",
            DevicePlatform = "android",
            StartedAt = now.AddHours(-1),
            AutoCloseAt = now.AddHours(6),
            LastCommunicationAt = now.AddMinutes(-1),
            Status = DeliveryWorkSessionStatus.Active,
        });
        await db.SaveChangesAsync();

        var loginUser = new User
        {
            Id = 1,
            BranchId = 7,
            Branch = branch,
            Role = UserRole.Deliveryman,
            Name = "Domiciliario",
            Email = "domiciliario@example.com",
            Phone = "3000000000",
            PasswordHash = "hash",
            Active = true,
        };
        var auth = new Mock<IAuthRepository>();
        auth.Setup(x => x.GetUserByEmailAsync(loginUser.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(loginUser);
        auth.Setup(x => x.ValidatePasswordAsync(loginUser, "secret", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        RefreshToken? createdRefreshToken = null;
        var refreshTokens = new Mock<IRefreshTokenRepository>();
        refreshTokens
            .Setup(x => x.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, CancellationToken>((token, _) => createdRefreshToken = token)
            .Returns(Task.CompletedTask);

        var jwt = new Mock<IJwtService>();
        jwt.Setup(x => x.GenerateAccessToken(
                loginUser,
                It.IsAny<Guid?>(),
                "device-b"))
            .Returns("access-token");
        jwt.Setup(x => x.GenerateRefreshToken()).Returns("refresh-token");

        var mapper = new Mock<IMapper>();
        mapper.Setup(x => x.Map<UserInfoDto>(loginUser)).Returns(new UserInfoDto
        {
            Id = loginUser.Id,
            Name = loginUser.Name,
            Email = loginUser.Email,
        });

        var handler = new LoginHandler(
            auth.Object,
            refreshTokens.Object,
            jwt.Object,
            mapper.Object,
            new FakeClock(now),
            db,
            Mock.Of<IDeliveryAppVersionPolicy>());

        var result = await handler.Handle(new LoginCommand
        {
            Email = loginUser.Email,
            Password = "secret",
            DeviceInstallationId = "device-b",
            IpAddress = "127.0.0.1",
            DeliveryAppVersion = new DeliveryAppClientVersion(
                "1.2.5",
                11,
                "com.senorarroz.delivery_app"),
        }, default);

        var persistedUser = await db.Users.SingleAsync();
        var previousWorkSession = await db.DeliveryWorkSessions.SingleAsync();
        Assert.NotNull(persistedUser.ActiveSessionId);
        Assert.Equal(persistedUser.ActiveSessionId, createdRefreshToken?.SessionId);
        Assert.Equal(DeliveryWorkSessionStatus.Closed, previousWorkSession.Status);
        Assert.Equal(DeliveryWorkSessionEndReason.UserChange, previousWorkSession.EndReason);
        Assert.Equal("access-token", result.Token);
        refreshTokens.Verify(
            x => x.RevokeAllByUserIdAsync(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Refresh_ReplacedDeliverySession_IsRejected()
    {
        var oldSessionId = Guid.NewGuid();
        var currentSessionId = Guid.NewGuid();
        var now = new DateTime(2026, 7, 23, 15, 0, 0, DateTimeKind.Utc);
        var refreshToken = new RefreshToken
        {
            UserId = 1,
            SessionId = oldSessionId,
            Token = "old-refresh",
            ExpiresAt = now.AddDays(1),
        };

        var refreshTokens = new Mock<IRefreshTokenRepository>();
        refreshTokens.Setup(x => x.GetByTokenAsync("old-refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(refreshToken);
        var jwt = new Mock<IJwtService>();
        jwt.Setup(x => x.GetUserIdFromExpiredToken("old-access")).Returns(1);
        jwt.Setup(x => x.GetSessionIdFromExpiredToken("old-access")).Returns(oldSessionId);
        jwt.Setup(x => x.GetDeviceInstallationIdFromExpiredToken("old-access")).Returns("device-a");
        var auth = new Mock<IAuthRepository>();
        auth.Setup(x => x.GetUserByIdWithBranchAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = 1,
                Role = UserRole.Deliveryman,
                ActiveSessionId = currentSessionId,
                Active = true,
            });

        var handler = new RefreshTokenHandler(
            auth.Object,
            refreshTokens.Object,
            jwt.Object,
            Mock.Of<IMapper>(),
            new FakeClock(now),
            Mock.Of<IDeliveryAppVersionPolicy>());

        await Assert.ThrowsAsync<SessionReplacedException>(() => handler.Handle(
            new RefreshTokenCommand
            {
                Token = "old-access",
                RefreshToken = "old-refresh",
                IpAddress = "127.0.0.1",
            },
            default));
        refreshTokens.Verify(
            x => x.UpdateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Login_IncompatibleDeliveryApp_HasNoSessionSideEffects()
    {
        await using var db = CreateDb();
        var user = new User
        {
            Id = 1,
            BranchId = 7,
            Role = UserRole.Deliveryman,
            Name = "Domiciliario",
            Email = "delivery@example.com",
            Phone = "3000000000",
            PasswordHash = "hash",
            Active = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var auth = new Mock<IAuthRepository>();
        auth.Setup(x => x.GetUserByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        auth.Setup(x => x.ValidatePasswordAsync(user, "secret", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var policy = new Mock<IDeliveryAppVersionPolicy>();
        policy.Setup(x => x.EnsureCompatible(It.IsAny<DeliveryAppClientVersion?>()))
            .Throws(new DeliveryAppUpdateRequiredException("1.2.5", 11, "play"));
        var refreshTokens = new Mock<IRefreshTokenRepository>();
        var handler = new LoginHandler(
            auth.Object,
            refreshTokens.Object,
            Mock.Of<IJwtService>(),
            Mock.Of<IMapper>(),
            new FakeClock(DateTime.UtcNow),
            db,
            policy.Object);

        await Assert.ThrowsAsync<DeliveryAppUpdateRequiredException>(() => handler.Handle(
            new LoginCommand
            {
                Email = user.Email,
                Password = "secret",
                DeliveryAppVersion = new DeliveryAppClientVersion(
                    "1.2.4",
                    11,
                    "com.senorarroz.delivery_app"),
            },
            default));

        Assert.Null((await db.Users.SingleAsync()).ActiveSessionId);
        refreshTokens.Verify(
            x => x.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Superadmin)]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Kitchen)]
    public async Task Login_NonDeliveryRoles_DoNotRequireFlutterHeaders(UserRole role)
    {
        await using var db = CreateDb();
        var user = new User
        {
            Id = 1,
            BranchId = 7,
            Role = role,
            Name = "Usuario",
            Email = $"{role}@example.com",
            Phone = "3000000000",
            PasswordHash = "hash",
            Active = true,
        };
        var auth = new Mock<IAuthRepository>();
        auth.Setup(x => x.GetUserByEmailAsync(user.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        auth.Setup(x => x.ValidatePasswordAsync(user, "secret", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var refreshTokens = new Mock<IRefreshTokenRepository>();
        refreshTokens.Setup(x => x.RevokeAllByUserIdAsync(
                user.Id,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        refreshTokens.Setup(x => x.AddAsync(
                It.IsAny<RefreshToken>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var jwt = new Mock<IJwtService>();
        jwt.Setup(x => x.GenerateAccessToken(user, null, null)).Returns("access");
        jwt.Setup(x => x.GenerateRefreshToken()).Returns("refresh");
        var mapper = new Mock<IMapper>();
        mapper.Setup(x => x.Map<UserInfoDto>(user)).Returns(new UserInfoDto());
        var policy = new Mock<IDeliveryAppVersionPolicy>();
        var handler = new LoginHandler(
            auth.Object,
            refreshTokens.Object,
            jwt.Object,
            mapper.Object,
            new FakeClock(DateTime.UtcNow),
            db,
            policy.Object);

        var result = await handler.Handle(new LoginCommand
        {
            Email = user.Email,
            Password = "secret",
            IpAddress = "127.0.0.1",
        }, default);

        Assert.Equal("access", result.Token);
        policy.Verify(
            x => x.EnsureCompatible(It.IsAny<DeliveryAppClientVersion?>()),
            Times.Never);
    }

    [Fact]
    public async Task Refresh_IncompatibleDeliveryApp_DoesNotRevokeToken()
    {
        var now = DateTime.UtcNow;
        var sessionId = Guid.NewGuid();
        var refreshToken = new RefreshToken
        {
            UserId = 1,
            SessionId = sessionId,
            Token = "refresh",
            ExpiresAt = now.AddDays(1),
        };
        var refreshTokens = new Mock<IRefreshTokenRepository>();
        refreshTokens.Setup(x => x.GetByTokenAsync("refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(refreshToken);
        var jwt = new Mock<IJwtService>();
        jwt.Setup(x => x.GetUserIdFromExpiredToken("access")).Returns(1);
        jwt.Setup(x => x.GetSessionIdFromExpiredToken("access")).Returns(sessionId);
        var auth = new Mock<IAuthRepository>();
        auth.Setup(x => x.GetUserByIdWithBranchAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User
            {
                Id = 1,
                Role = UserRole.Deliveryman,
                ActiveSessionId = sessionId,
                Active = true,
            });
        var policy = new Mock<IDeliveryAppVersionPolicy>();
        policy.Setup(x => x.EnsureCompatible(It.IsAny<DeliveryAppClientVersion?>()))
            .Throws(new DeliveryAppUpdateRequiredException("1.2.5", 11, "play"));
        var handler = new RefreshTokenHandler(
            auth.Object,
            refreshTokens.Object,
            jwt.Object,
            Mock.Of<IMapper>(),
            new FakeClock(now),
            policy.Object);

        await Assert.ThrowsAsync<DeliveryAppUpdateRequiredException>(() => handler.Handle(
            new RefreshTokenCommand
            {
                Token = "access",
                RefreshToken = "refresh",
                DeliveryAppVersion = new DeliveryAppClientVersion(
                    "1.2.4",
                    11,
                    "com.senorarroz.delivery_app"),
            },
            default));

        Assert.Null(refreshToken.RevokedAt);
        refreshTokens.Verify(
            x => x.UpdateAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EndSession_OldDeviceCannotClearCurrentSession()
    {
        await using var db = CreateDb();
        var currentSessionId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = 1,
            BranchId = 7,
            Role = UserRole.Deliveryman,
            Name = "Domiciliario",
            Email = "domiciliario@example.com",
            Phone = "3000000000",
            PasswordHash = "hash",
            Active = true,
            ActiveSessionId = currentSessionId,
        });
        await db.SaveChangesAsync();
        var repository = new AuthRepository(db, Mock.Of<IPasswordService>());

        Assert.True(await repository.IsSessionCurrentAsync(1, currentSessionId));
        Assert.False(await repository.IsSessionCurrentAsync(1, Guid.NewGuid()));

        await repository.EndSessionIfCurrentAsync(1, Guid.NewGuid());
        Assert.Equal(currentSessionId, (await db.Users.SingleAsync()).ActiveSessionId);

        await repository.EndSessionIfCurrentAsync(1, currentSessionId);
        Assert.Null((await db.Users.SingleAsync()).ActiveSessionId);
    }

    [Fact]
    public async Task CloseWorkSession_OldDeviceCannotCloseCurrentDeviceSession()
    {
        await using var db = CreateDb();
        var now = new DateTime(2026, 7, 23, 15, 0, 0, DateTimeKind.Utc);
        db.DeliveryWorkSessions.Add(new DeliveryWorkSession
        {
            Id = 10,
            DeliverymanId = 1,
            BranchId = 7,
            DeviceInstallationId = "device-b",
            DevicePlatform = "android",
            StartedAt = now.AddMinutes(-30),
            AutoCloseAt = now.AddHours(6),
            LastCommunicationAt = now,
            Status = DeliveryWorkSessionStatus.Active,
        });
        await db.SaveChangesAsync();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.Id).Returns(1);
        currentUser.SetupGet(x => x.DeviceInstallationId).Returns("device-a");
        var handler = new CloseMyDeliveryWorkSessionHandler(
            db,
            currentUser.Object,
            new FakeClock(now));

        await handler.Handle(new CloseMyDeliveryWorkSessionCommand(), default);

        Assert.Equal(
            DeliveryWorkSessionStatus.Active,
            (await db.DeliveryWorkSessions.SingleAsync()).Status);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
