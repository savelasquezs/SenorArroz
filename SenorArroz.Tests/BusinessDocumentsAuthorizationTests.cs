using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using SenorArroz.API.Controllers;

namespace SenorArroz.Tests;

public class BusinessDocumentsAuthorizationTests
{
    [Theory]
    [InlineData(nameof(BusinessDocumentsController.CreateDocument))]
    [InlineData(nameof(BusinessDocumentsController.UpdateDocument))]
    [InlineData(nameof(BusinessDocumentsController.DeleteDocument))]
    public void Mutations_are_restricted_to_superadmin(string methodName)
    {
        var method = typeof(BusinessDocumentsController).GetMethod(methodName)
            ?? throw new InvalidOperationException($"Method {methodName} not found.");
        var authorize = method.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Superadmin", authorize!.Roles);
    }

    [Fact]
    public void Public_download_allows_anonymous_access()
    {
        var method = typeof(BusinessDocumentsController)
            .GetMethod(nameof(BusinessDocumentsController.Download))
            ?? throw new InvalidOperationException("Download method not found.");

        Assert.NotNull(method.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void Document_catalog_requires_authentication()
    {
        Assert.NotNull(
            typeof(BusinessDocumentsController).GetCustomAttribute<AuthorizeAttribute>());
    }
}
