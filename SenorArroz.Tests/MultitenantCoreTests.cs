using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Auth.Commands;
using SenorArroz.Application.Features.Auth.DTOs;
using SenorArroz.Application.Features.Deliverymen.Commands;
using SenorArroz.Application.Features.Users.Commands;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Interfaces.Services;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public sealed class MultitenantCoreTests
{
    private static readonly Guid TenantOnePublicId = Guid.Parse("86ae3576-33db-483a-9a83-d9087a660651");

    [Fact]
    public async Task CreateUser_derives_tenant_from_persisted_branch()
    {
        await using var db = CreateDb();
        db.Tenants.Add(CreateTenant(1));
        db.Branches.Add(CreateBranch(10, 1));
        await db.SaveChangesAsync();

        var dto = new CreateUserDto
        {
            BranchId = 10,
            Role = UserRole.Cashier,
            Name = "Caja",
            Email = "caja@test.local",
            Phone = "3000000000",
            Password = "secret",
        };
        User? persisted = null;
        var users = new Mock<IUserRepository>();
        users.Setup(x => x.EmailExistsAsync(dto.Email, null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        users.Setup(x => x.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((user, _) => persisted = user)
            .ReturnsAsync((User user, CancellationToken _) => user);
        users.Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        var mapper = new Mock<IMapper>();
        mapper.Setup(x => x.Map<User>(dto)).Returns(new User
        {
            BranchId = dto.BranchId,
            Role = dto.Role,
            Name = dto.Name,
            Email = dto.Email,
            Phone = dto.Phone,
        });
        mapper.Setup(x => x.Map<UserDto>(It.IsAny<User>())).Returns(new UserDto());
        var password = new Mock<IPasswordService>();
        password.Setup(x => x.HashPassword(dto.Password)).Returns("hash");

        var handler = new CreateUserHandler(
            users.Object,
            password.Object,
            mapper.Object,
            CurrentUser("superadmin", 10, 1),
            new TestBranchContext(10),
            CurrentTenant(1),
            db);

        await handler.Handle(new CreateUserCommand(dto), default);

        Assert.NotNull(persisted);
        Assert.Equal(1, persisted.TenantId);
        Assert.Equal(10, persisted.BranchId);
    }

    [Fact]
    public async Task CreateUser_rejects_branch_from_another_tenant()
    {
        await using var db = CreateDb();
        db.Tenants.AddRange(CreateTenant(1), CreateTenant(2));
        db.Branches.Add(CreateBranch(20, 2));
        await db.SaveChangesAsync();

        var dto = new CreateUserDto
        {
            BranchId = 20,
            Role = UserRole.Cashier,
            Email = "cross@test.local",
            Password = "secret",
        };
        var handler = new CreateUserHandler(
            Mock.Of<IUserRepository>(),
            Mock.Of<IPasswordService>(),
            Mock.Of<IMapper>(),
            CurrentUser("superadmin", 10, 1),
            new TestBranchContext(20),
            CurrentTenant(1),
            db);

        await Assert.ThrowsAsync<BusinessException>(() =>
            handler.Handle(new CreateUserCommand(dto), default));
    }

    [Fact]
    public void Model_uses_composite_tenant_ownership_foreign_keys()
    {
        using var db = CreateDb();
        var userType = db.Model.FindEntityType(typeof(User))!;
        var userBranch = userType.GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(Branch));
        Assert.Equal(new[] { nameof(User.TenantId), nameof(User.BranchId) }, userBranch.Properties.Select(x => x.Name));

        AssertTokenForeignKey<RefreshToken>(db, nameof(RefreshToken.TenantId), nameof(RefreshToken.UserId));
        AssertTokenForeignKey<PasswordResetToken>(db, nameof(PasswordResetToken.TenantId), nameof(PasswordResetToken.UserId));
        AssertTokenForeignKey<UserDeviceToken>(db, nameof(UserDeviceToken.TenantId), nameof(UserDeviceToken.UserId));
    }

    [Fact]
    public void Jwt_contains_backend_tenant_claims_and_existing_tenant_one_remains_valid()
    {
        var now = DateTime.UtcNow;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:SecretKey"] = "a-secure-test-key-with-more-than-thirty-two-bytes",
            ["JwtSettings:Issuer"] = "tests",
            ["JwtSettings:Audience"] = "tests",
            ["JwtSettings:AccessTokenExpirationMinutes"] = "60",
        }).Build();
        var user = CreateAuthenticatedUser(1, 10);
        var jwt = new JwtService(configuration, new FakeClock(now));

        var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt.GenerateAccessToken(user));

        Assert.Equal("1", token.Claims.Single(x => x.Type == "tenant_id").Value);
        Assert.Equal(TenantOnePublicId.ToString("D"), token.Claims.Single(x => x.Type == "tenant_public_id").Value);
        Assert.Equal("1", token.Claims.Single(x => x.Type == "tenant_access_version").Value);
        Assert.Equal("10", token.Claims.Single(x => x.Type == "branch_id").Value);
    }

    [Fact]
    public async Task Refresh_preserves_user_and_token_tenant()
    {
        var now = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);
        var user = CreateAuthenticatedUser(1, 10);
        var current = new RefreshToken
        {
            TenantId = 1,
            UserId = user.Id,
            Token = "refresh",
            ExpiresAt = now.AddDays(1),
        };
        RefreshToken? replacement = null;
        var refreshTokens = new Mock<IRefreshTokenRepository>();
        refreshTokens.Setup(x => x.GetByTokenAsync("refresh", It.IsAny<CancellationToken>())).ReturnsAsync(current);
        refreshTokens.Setup(x => x.UpdateAsync(current, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        refreshTokens.Setup(x => x.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, CancellationToken>((token, _) => replacement = token)
            .Returns(Task.CompletedTask);
        var auth = new Mock<IAuthRepository>();
        auth.Setup(x => x.GetUserByIdWithBranchAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var jwt = new Mock<IJwtService>();
        jwt.Setup(x => x.GetUserIdFromExpiredToken("access")).Returns(user.Id);
        jwt.Setup(x => x.GetTenantIdFromExpiredToken("access")).Returns(1);
        jwt.Setup(x => x.GenerateAccessToken(user, null, null)).Returns("new-access");
        jwt.Setup(x => x.GenerateRefreshToken()).Returns("new-refresh");
        var mapper = new Mock<IMapper>();
        mapper.Setup(x => x.Map<UserInfoDto>(user)).Returns(new UserInfoDto());
        var handler = new RefreshTokenHandler(
            auth.Object,
            refreshTokens.Object,
            jwt.Object,
            mapper.Object,
            new FakeClock(now),
            Mock.Of<IDeliveryAppVersionPolicy>());

        await handler.Handle(new RefreshTokenCommand
        {
            Token = "access",
            RefreshToken = "refresh",
            IpAddress = "127.0.0.1",
        }, default);

        Assert.NotNull(replacement);
        Assert.Equal(1, replacement.TenantId);
        Assert.Equal(user.Id, replacement.UserId);
    }

    [Fact]
    public async Task Device_and_password_reset_tokens_derive_tenant_from_user()
    {
        await using var db = CreateDb();
        var tenant = CreateTenant(2);
        var branch = CreateBranch(20, 2);
        var user = CreateAuthenticatedUser(2, 20);
        user.Id = 30;
        user.Tenant = tenant;
        user.Branch = branch;
        db.AddRange(tenant, branch, user);
        await db.SaveChangesAsync();
        var handler = new RegisterDeviceTokenHandler(
            db,
            CurrentUser("deliveryman", 20, 2, user.Id),
            new FakeClock(DateTime.UtcNow));

        await handler.Handle(new RegisterDeviceTokenCommand
        {
            Token = "fcm-token",
            Platform = "android",
        }, default);

        Assert.Equal(2, (await db.UserDeviceTokens.SingleAsync()).TenantId);
        Assert.Equal(2, PasswordResetToken.Create(2, user.Id, user.Email, 60, DateTime.UtcNow).TenantId);
    }

    [Fact]
    public async Task Branch_context_rejects_selected_branch_from_another_tenant()
    {
        await using var db = CreateDb();
        db.Tenants.AddRange(CreateTenant(1), CreateTenant(2));
        db.Branches.Add(CreateBranch(20, 2));
        await db.SaveChangesAsync();
        var http = new DefaultHttpContext();
        http.Request.Headers[BranchContextService.HeaderName] = "20";
        var service = new BranchContextService(
            new HttpContextAccessor { HttpContext = http },
            CurrentUser("superadmin", 10, 1),
            db,
            CurrentTenant(1));

        Assert.Throws<BranchAccessDeniedException>(() => _ = service.SelectedBranchId);
    }

    [Fact]
    public void CurrentTenant_uses_claim_for_authenticated_requests_and_server_fallback_for_public_work()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Multitenancy:DefaultTenantId"] = "1",
            ["Multitenancy:DefaultTenantPublicId"] = TenantOnePublicId.ToString("D"),
            ["Multitenancy:DefaultTenantAccessVersion"] = "1",
        }).Build();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var current = new CurrentTenantService(accessor, configuration);
        Assert.Equal(1, current.TenantId);

        accessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("tenant_id", "2"),
            new Claim("tenant_public_id", Guid.Empty.ToString("D")),
            new Claim("tenant_access_version", "7"),
        }, "test"));

        Assert.Equal(2, current.TenantId);
        Assert.Equal(Guid.Empty, current.TenantPublicId);
        Assert.Equal(7, current.AccessVersion);
    }

    private static void AssertTokenForeignKey<T>(ApplicationDbContext db, params string[] properties)
    {
        var foreignKey = db.Model.FindEntityType(typeof(T))!.GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == typeof(User));
        Assert.Equal(properties, foreignKey.Properties.Select(x => x.Name));
    }

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Tenant CreateTenant(int id) => new()
    {
        Id = id,
        PublicId = id == 1 ? TenantOnePublicId : Guid.NewGuid(),
        Name = $"Tenant {id}",
        Slug = $"tenant-{id}",
        IsActive = true,
        Status = TenantStatus.Active,
        AccessVersion = 1,
    };

    private static Branch CreateBranch(int id, int tenantId) => new()
    {
        Id = id,
        TenantId = tenantId,
        Name = $"Branch {id}",
        Address = "Calle 1",
        Phone1 = "3000000000",
    };

    private static User CreateAuthenticatedUser(int tenantId, int branchId)
    {
        var tenant = CreateTenant(tenantId);
        var branch = CreateBranch(branchId, tenantId);
        branch.Tenant = tenant;
        return new User
        {
            Id = 99,
            TenantId = tenantId,
            Tenant = tenant,
            BranchId = branchId,
            Branch = branch,
            Role = UserRole.Admin,
            Name = "Admin",
            Email = "admin@test.local",
            Phone = "3000000000",
            PasswordHash = "hash",
            Active = true,
        };
    }

    private static ICurrentTenant CurrentTenant(int tenantId)
    {
        var current = new Mock<ICurrentTenant>();
        current.SetupGet(x => x.TenantId).Returns(tenantId);
        current.SetupGet(x => x.HasTenant).Returns(tenantId > 0);
        return current.Object;
    }

    private static ICurrentUser CurrentUser(
        string role,
        int branchId,
        int tenantId,
        int userId = 1)
    {
        var current = new Mock<ICurrentUser>();
        current.SetupGet(x => x.Id).Returns(userId);
        current.SetupGet(x => x.Role).Returns(role);
        current.SetupGet(x => x.BranchId).Returns(branchId);
        current.SetupGet(x => x.TenantId).Returns(tenantId);
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        return current.Object;
    }
}
