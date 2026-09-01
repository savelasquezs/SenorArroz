using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public class StorefrontCustomerAuthServiceTests
{
    [Fact]
    public async Task RequestAndVerify_UsesAuthenticationTemplateAndReturnsSavedCustomerData()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var branch = new Branch { Id = 1, Name = "Centro", Address = "Calle 1", Phone1 = "3000000000" };
        var customer = new Customer { Id = 10, BranchId = 1, Branch = branch, Name = "Santiago", Phone1 = "3001234567", Active = true };
        customer.Addresses.Add(new Address
        {
            Id = 20,
            CustomerId = customer.Id,
            Customer = customer,
            Label = "Casa",
            AddressText = "Cra 65 # 95-20",
            DeliveryFee = 5000,
            IsPrimary = true
        });
        db.AddRange(branch, customer, new WhatsAppBranchSetting
        {
            Id = 1,
            BranchId = branch.Id,
            Branch = branch,
            PhoneNumberId = "phone-id",
            AccessToken = "access-token",
            IsActive = true,
            IsVerified = true
        });
        await db.SaveChangesAsync();

        string? sentCode = null;
        var cloud = new Mock<IWhatsAppCloudClient>();
        cloud.Setup(x => x.SendAuthenticationTemplateMessageAsync(
                "phone-id", "access-token", "573001234567", "customers_web_authentication", "es",
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string, string, CancellationToken>((_, _, _, _, _, code, _) => sentCode = code)
            .ReturnsAsync(new WhatsAppCloudSendResult(true, "wamid.1", null));
        var service = new StorefrontCustomerAuthService(
            db,
            cloud.Object,
            new FakeClock(new DateTime(2026, 8, 31, 15, 0, 0, DateTimeKind.Utc)),
            Options.Create(new StorefrontCustomerAuthOptions
            {
                TenantId = 1,
                AuthenticationBranchId = 1,
                TemplateName = "customers_web_authentication",
                TemplateLanguage = "es",
                HmacSecret = "storefront-auth-test-secret-32-bytes-minimum"
            }),
            Mock.Of<ILogger<StorefrontCustomerAuthService>>());

        var requested = await service.RequestCodeAsync("300 123 4567", "127.0.0.1", default);

        Assert.Matches("^\\d{6}$", sentCode!);
        var challenge = Assert.Single(db.StorefrontCustomerAuthChallenges);
        Assert.NotEqual(sentCode, challenge.CodeHmac);
        Assert.Equal(64, challenge.CodeHmac.Length);

        var verified = await service.VerifyCodeAsync(requested.ChallengeId, sentCode!, default);

        Assert.NotEmpty(verified.SessionToken);
        Assert.True(verified.CustomerSession.ExistingCustomer);
        Assert.Equal("Santiago", verified.CustomerSession.Customer!.Name);
        var address = Assert.Single(verified.CustomerSession.Addresses);
        Assert.Equal("Casa", address.Label);
        Assert.Equal(5000, address.DeliveryFee);
        await Assert.ThrowsAsync<StorefrontAuthInvalidCodeException>(() =>
            service.VerifyCodeAsync(requested.ChallengeId, sentCode!, default));
    }

    [Fact]
    public async Task Verify_DuplicateActivePhoneBlocksPrivateCustomerData()
    {
        await using var db = CreateDb();
        var branch = SeedAuthenticationBranch(db);
        db.Customers.AddRange(
            new Customer { Id = 10, BranchId = branch.Id, Branch = branch, Name = "Cliente uno", Phone1 = "3001112233", Active = true },
            new Customer { Id = 11, BranchId = branch.Id, Branch = branch, Name = "Cliente dos", Phone2 = "3001112233", Active = true });
        await db.SaveChangesAsync();
        var (service, cloud) = CreateService(db);

        var requested = await service.RequestCodeAsync("3001112233", "127.0.0.1", default);
        var code = Assert.IsType<string>(Assert.Single(cloud.Invocations).Arguments[5]);
        var verified = await service.VerifyCodeAsync(requested.ChallengeId, code, default);

        Assert.True(verified.CustomerSession.AmbiguousCustomer);
        Assert.False(verified.CustomerSession.ExistingCustomer);
        Assert.Null(verified.CustomerSession.Customer);
        Assert.Empty(verified.CustomerSession.Addresses);
    }

    [Fact]
    public async Task Verify_IgnoresInactiveMatchAndFindsActiveSecondaryPhone()
    {
        await using var db = CreateDb();
        var branch = SeedAuthenticationBranch(db);
        var otherBranch = new Branch { Id = 2, Name = "Otra sede", Address = "Calle 2", Phone1 = "3000000002" };
        db.Branches.Add(otherBranch);
        db.Customers.AddRange(
            new Customer { Id = 10, BranchId = branch.Id, Branch = branch, Name = "Inactivo", Phone1 = "3004445566", Active = false },
            new Customer { Id = 11, BranchId = otherBranch.Id, Branch = otherBranch, Name = "Activo", Phone1 = "3009998877", Phone2 = "3004445566", Active = true });
        await db.SaveChangesAsync();
        var (service, cloud) = CreateService(db);

        var requested = await service.RequestCodeAsync("3004445566", "127.0.0.1", default);
        var code = Assert.IsType<string>(Assert.Single(cloud.Invocations).Arguments[5]);
        var verified = await service.VerifyCodeAsync(requested.ChallengeId, code, default);

        Assert.True(verified.CustomerSession.ExistingCustomer);
        Assert.False(verified.CustomerSession.AmbiguousCustomer);
        Assert.Equal("Activo", verified.CustomerSession.Customer!.Name);
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Branch SeedAuthenticationBranch(ApplicationDbContext db)
    {
        var branch = new Branch { Id = 1, Name = "Centro", Address = "Calle 1", Phone1 = "3000000000" };
        db.AddRange(branch, new WhatsAppBranchSetting
        {
            Id = 1,
            BranchId = branch.Id,
            Branch = branch,
            PhoneNumberId = "phone-id",
            AccessToken = "access-token",
            IsActive = true,
            IsVerified = true,
        });
        return branch;
    }

    private static (StorefrontCustomerAuthService Service, Mock<IWhatsAppCloudClient> Cloud) CreateService(ApplicationDbContext db)
    {
        var cloud = new Mock<IWhatsAppCloudClient>();
        cloud.Setup(x => x.SendAuthenticationTemplateMessageAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppCloudSendResult(true, "wamid.1", null));
        return (new StorefrontCustomerAuthService(
            db,
            cloud.Object,
            new FakeClock(new DateTime(2026, 8, 31, 15, 0, 0, DateTimeKind.Utc)),
            Options.Create(new StorefrontCustomerAuthOptions
            {
                TenantId = 1,
                AuthenticationBranchId = 1,
                HmacSecret = "storefront-auth-test-secret-32-bytes-minimum",
            }),
            Mock.Of<ILogger<StorefrontCustomerAuthService>>()), cloud);
    }
}
