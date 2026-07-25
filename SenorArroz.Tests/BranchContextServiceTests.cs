using Microsoft.AspNetCore.Http;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public sealed class BranchContextServiceTests
{
    [Fact]
    public void Superadmin_uses_header_as_effective_branch()
    {
        var context = Create("superadmin", assignedBranchId: 1, header: "2");

        Assert.Equal(2, context.EffectiveBranchId);
        Assert.Equal(2, context.RequireBranch());
    }

    [Fact]
    public void Superadmin_without_header_can_use_legacy_explicit_branch()
    {
        var context = Create("superadmin", assignedBranchId: 1);

        Assert.Equal(2, context.RequireBranch(2));
        Assert.Null(context.EffectiveBranchId);
    }

    [Fact]
    public void Superadmin_rejects_payload_that_differs_from_header()
    {
        var context = Create("superadmin", assignedBranchId: 1, header: "2");

        Assert.Throws<BranchScopeMismatchException>(() => context.RequireBranch(1));
    }

    [Fact]
    public void Non_superadmin_always_uses_assigned_branch()
    {
        var context = Create("admin", assignedBranchId: 7);

        Assert.Equal(7, context.RequireBranch());
        Assert.Equal(7, context.ResolveOptional());
    }

    [Fact]
    public void Non_superadmin_cannot_override_assigned_branch()
    {
        var context = Create("cashier", assignedBranchId: 7);

        Assert.Throws<BranchAccessDeniedException>(() => context.ResolveOptional(8));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    public void Invalid_header_is_rejected(string value)
    {
        var context = Create("superadmin", assignedBranchId: 1, header: value, includeHeader: true);

        if (string.IsNullOrWhiteSpace(value))
        {
            Assert.Throws<BranchContextRequiredException>(() => context.RequireBranch());
            return;
        }

        Assert.Throws<BranchContextRequiredException>(() => _ = context.SelectedBranchId);
    }

    [Fact]
    public void Superadmin_write_without_any_context_is_rejected()
    {
        var context = Create("superadmin", assignedBranchId: 1);

        Assert.Throws<BranchContextRequiredException>(() => context.RequireBranch());
    }

    private static BranchContextService Create(
        string role,
        int assignedBranchId,
        string? header = null,
        bool includeHeader = false)
    {
        var httpContext = new DefaultHttpContext();
        if (includeHeader || header is not null)
            httpContext.Request.Headers[BranchContextService.HeaderName] = header ?? string.Empty;

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        return new BranchContextService(
            accessor,
            new TestCurrentUser(role, assignedBranchId));
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public TestCurrentUser(string role, int branchId)
        {
            Role = role;
            BranchId = branchId;
        }

        public int Id => 10;
        public string Role { get; }
        public int BranchId { get; }
        public bool IsAuthenticated => true;
    }
}
