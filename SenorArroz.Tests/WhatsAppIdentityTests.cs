using System.Text.Json;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Customers.Commands;
using SenorArroz.Application.Features.Customers.Validators;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Tests;

public class WhatsAppIdentityTests
{
    [Theory]
    [InlineData("VillaA_999", "@villaa_999")]
    [InlineData("  @CLIENTE.Uno  ", "@cliente.uno")]
    public void Username_IsNormalizedWithAtAndLowercase(string input, string expected)
    {
        Assert.Equal(expected, WhatsAppIdentityNormalizer.NormalizeUsername(input));
        Assert.True(WhatsAppIdentityNormalizer.IsValidUsername(input));
    }

    [Fact]
    public void CustomerValidator_AllowsUsernameWithoutPhone()
    {
        var result = new CreateCustomerValidator().Validate(new CreateCustomerCommand
        {
            Name = "Cliente WhatsApp",
            WhatsAppUsername = "@cliente_99",
            BranchId = 1
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void CustomerValidator_RejectsMissingIdentityAndSecondaryPhoneWithoutPrimary()
    {
        var validator = new CreateCustomerValidator();

        Assert.False(validator.Validate(new CreateCustomerCommand { Name = "Cliente Uno", BranchId = 1 }).IsValid);
        Assert.False(validator.Validate(new CreateCustomerCommand
        {
            Name = "Cliente Uno",
            Phone2 = "3001234567",
            WhatsAppUsername = "@cliente_99",
            BranchId = 1
        }).IsValid);
    }

    [Fact]
    public void WebhookReader_ReadsLegacyPhoneOnlyPayload()
    {
        using var document = JsonDocument.Parse("""{"contacts":[{"wa_id":"573001234567","profile":{"name":"María"}}],"messages":[{"from":"573001234567"}]}""");
        var root = document.RootElement;

        var identity = WhatsAppWebhookIdentityReader.Read(root, root.GetProperty("messages")[0]);

        Assert.Equal("573001234567", identity.PhoneNumber);
        Assert.Null(identity.UserId);
        Assert.Null(identity.Username);
        Assert.Equal("María", identity.ContactName);
    }

    [Fact]
    public void WebhookReader_ReadsBsuidAndUsernameWhenPhoneIsEmpty()
    {
        using var document = JsonDocument.Parse("""{"contacts":[{"wa_id":"","user_id":"user.abc123","profile":{"name":"Villa","username":"VillaA_999"}}],"messages":[{"from":"","from_user_id":"user.abc123"}]}""");
        var root = document.RootElement;

        var identity = WhatsAppWebhookIdentityReader.Read(root, root.GetProperty("messages")[0]);

        Assert.Null(identity.PhoneNumber);
        Assert.Equal("user.abc123", identity.UserId);
        Assert.Equal("@villaa_999", identity.Username);
        Assert.Equal("Villa", identity.ContactName);
    }

    [Fact]
    public void RecipientResolver_PrefersPhoneAndFallsBackToBsuid()
    {
        Assert.Equal("573001234567", WhatsAppRecipientResolver.Resolve("573001234567", "user.abc123"));
        Assert.Equal("user.abc123", WhatsAppRecipientResolver.Resolve(null, "user.abc123"));
        Assert.Null(WhatsAppRecipientResolver.Resolve(new WhatsAppConversation()));
    }
}
