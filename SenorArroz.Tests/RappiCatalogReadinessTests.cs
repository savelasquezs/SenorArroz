using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Integrations;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public sealed class RappiCatalogReadinessTests
{
    [Theory]
    [InlineData("sandbox", false, "https://api.dev.rappi.com", "https://api.dev.rappi.com", true)]
    [InlineData("production", true, "https://api.rappi.com.co", "https://services.rappi.com", true)]
    [InlineData("production", false, "https://api.rappi.com.co", "https://services.rappi.com", false)]
    [InlineData("production", true, "https://api.dev.rappi.com", "https://services.rappi.com", false)]
    [InlineData("sandbox", false, "https://api.dev.rappi.com", "https://services.rappi.com", false)]
    public void Environment_configuration_rejects_mixed_or_unapproved_production(
        string environment, bool enabled, string auth, string api, bool expected)
    {
        Assert.Equal(expected, new RappiOptions
        {
            Environment = environment, ProductionEnabled = enabled,
            AuthUrl = auth + "/restaurants/auth/v1/token/login/integrations",
            ApiBaseUrl = api + "/api/v2/restaurants-integrations-public-api"
        }.HasValidEnvironmentConfiguration());
    }

    [Fact]
    public async Task Production_configuration_cannot_expose_sandbox_or_enable_unapproved_ready()
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        var actor = Mock.Of<ICurrentUser>(x => x.Role == "Admin" && x.BranchId == 1);
        var controller = new RappiIntegrationsController(db, actor, new FakeClock(RappiCatalogTestData.Now),
            Mock.Of<IIntegrationSecretProtector>(), provider.Object, Mock.Of<IRappiOrderProcessor>(),
            Options.Create(new ApiPublicOptions()), Mock.Of<IBackgroundWorkSignal<RappiWork>>(), new TestTenantContext(),
            Options.Create(new RappiOptions { Environment = "production" }));
        var apps = Assert.IsType<OkObjectResult>((await controller.GetApps(1, default)).Result);
        var json = JsonSerializer.Serialize(apps.Value);
        Assert.Contains("production", json);
        Assert.DoesNotContain("900173116", json);
        var dto = RappiCatalogTestData.ConnectionDto();
        dto.Stores[0].ManualReadyForPickupEnabled = true;
        Assert.IsType<BadRequestObjectResult>((await controller.Upsert(1, dto, default)).Result);
        Assert.IsType<NotFoundResult>(await controller.Webhook((await db.DeliveryAppConnections.SingleAsync()).PublicId,
            "PING", default));
        provider.VerifyNoOtherCalls();
    }

    [PostgreSqlIntegrationFact]
    public async Task Worker_does_not_send_sandbox_availability_with_production_environment()
    {
        await using var fixture = await RappiIsolatedBusinessDatabase.CreateAsync();
        await using var db = fixture.Db();
        db.RappiAvailabilityStates.Add(new RappiAvailabilityState
        {
            ConnectionId = 11, StoreId = 11, ProductMappingId = 11, DesiredAvailable = true,
            Status = "pending", CreatedAt = RappiCatalogTestData.Now, UpdatedAt = RappiCatalogTestData.Now
        });
        await db.SaveChangesAsync();
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        await RappiCatalogTestData.WorkerAsync("ProcessAvailabilityAsync", db, provider.Object, "production");
        Assert.Equal("pending", (await db.RappiAvailabilityStates.SingleAsync()).Status);
        provider.VerifyNoOtherCalls();
    }
    [Theory]
    [InlineData("disabled")]
    [InlineData("unverified")]
    [InlineData("deselected")]
    [InlineData("other-provider")]
    [InlineData("foreign-store-connection")]
    [InlineData("foreign-mapping-connection")]
    [InlineData("blank-integration-id")]
    public async Task Previously_queued_availability_does_not_send_ineligible_targets(string condition)
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        var connection = await db.DeliveryAppConnections.SingleAsync();
        var mapping = await db.DeliveryAppProductMappings.SingleAsync();
        var store = await db.DeliveryAppStores.SingleAsync();
        if (condition == "disabled") connection.IsActive = false;
        if (condition == "unverified") connection.IsVerified = false;
        if (condition == "deselected") mapping.IsSelected = false;
        if (condition == "other-provider") connection.Provider = "another-provider";
        if (condition == "foreign-store-connection") store.ConnectionId = 99;
        if (condition == "foreign-mapping-connection") mapping.ConnectionId = 99;
        if (condition == "blank-integration-id") store.StoreIntegrationId = " ";
        db.RappiAvailabilityStates.Add(RappiCatalogTestData.Availability());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);

        await RappiCatalogTestData.WorkerAsync("ProcessAvailabilityAsync", db, provider.Object);

        provider.VerifyNoOtherCalls();
        Assert.Equal(0, (await db.RappiAvailabilityStates.SingleAsync()).AttemptCount);
    }

    [Fact]
    public async Task Availability_never_dispatches_cross_tenant_link_even_in_system_scope()
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        using var system = TestTenantContext.Default.BeginSystemScope();
        var state = RappiCatalogTestData.Availability();
        state.TenantId = 2; // Deliberate corrupt relation, only possible in this EF InMemory fixture.
        db.RappiAvailabilityStates.Add(state);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        await RappiCatalogTestData.WorkerAsync("ProcessAvailabilityAsync", db, provider.Object);
        provider.VerifyNoOtherCalls();
        Assert.Equal(0, (await db.RappiAvailabilityStates.SingleAsync()).AttemptCount);
    }

    [Fact]
    public async Task Availability_retries_eligible_parent_and_child_without_changing_desired_stock()
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        var connection = await db.DeliveryAppConnections.SingleAsync();
        db.DeliveryAppStores.Add(new DeliveryAppStore { Id = 2, Connection = connection, RappiStoreId = "child", StoreIntegrationId = "child-external", Name = "Child" });
        db.RappiAvailabilityStates.AddRange(RappiCatalogTestData.Availability(),
            RappiCatalogTestData.Availability(storeId: 2, desired: false));
        await db.SaveChangesAsync();
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.Setup(x => x.SetAvailabilityAsync(It.IsAny<IReadOnlyCollection<RappiAvailabilityRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RappiOperationResult(false, 503));
        await RappiCatalogTestData.WorkerAsync("ProcessAvailabilityAsync", db, provider.Object);
        provider.Verify(x => x.SetAvailabilityAsync(It.Is<IReadOnlyCollection<RappiAvailabilityRequest>>(r => r.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
        var rows = await db.RappiAvailabilityStates.OrderBy(x => x.StoreId).ToListAsync();
        Assert.True(rows[0].DesiredAvailable);
        Assert.False(rows[1].DesiredAvailable);
        Assert.All(rows, x => { Assert.Equal(1, x.AttemptCount); Assert.Equal("pending", x.Status); Assert.NotNull(x.NextAttemptAt); });
    }

    [Fact]
    public async Task Internal_poll_approval_targets_its_publication_not_the_latest()
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        var old = RappiCatalogTestData.Publication(1, RappiCatalogTestData.Now.AddHours(-1));
        var newer = RappiCatalogTestData.Publication(2, RappiCatalogTestData.Now);
        db.RappiMenuPublications.AddRange(old, newer);
        await db.SaveChangesAsync();
        await RappiCatalogTestData.ApplyMenuAsync(db, "MENU_APPROVED", $"MENU_APPROVED_POLL:{old.Id}");
        Assert.Equal("approved", old.Status);
        Assert.Equal("submitted", newer.Status);
    }

    [Theory]
    [InlineData("MENU_APPROVED", "submitted")]
    [InlineData("MENU_REJECTED", "approval_review_required")]
    public async Task Uncorrelated_callback_is_a_visible_hint_not_approval_of_newer_publication(string eventType, string expectedStatus)
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        var old = RappiCatalogTestData.Publication(1, RappiCatalogTestData.Now.AddHours(-1));
        old.Status = "approved";
        var newer = RappiCatalogTestData.Publication(2, RappiCatalogTestData.Now);
        db.RappiMenuPublications.AddRange(old, newer);
        await db.SaveChangesAsync();
        await RappiCatalogTestData.ApplyMenuAsync(db, eventType, $"{eventType}:123:hash");
        Assert.Equal(expectedStatus, newer.Status);
        Assert.NotNull((await db.DeliveryAppConnections.SingleAsync()).LastError);
    }

    [Fact]
    public async Task Rejection_without_correlation_holds_approval_even_if_poll_success_was_already_queued()
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        var publication = RappiCatalogTestData.Publication(1, RappiCatalogTestData.Now);
        db.RappiMenuPublications.Add(publication);
        await db.SaveChangesAsync();
        await RappiCatalogTestData.ApplyMenuAsync(db, "MENU_REJECTED", "MENU_REJECTED:123:hash");
        await RappiCatalogTestData.ApplyMenuAsync(db, "MENU_APPROVED", "MENU_APPROVED_POLL:1");
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        await RappiCatalogTestData.WorkerAsync("RecoverMenuApprovalsAsync", db, provider.Object);
        Assert.Equal("approval_review_required", publication.Status);
        Assert.Null(publication.CompletedAt);
        Assert.NotNull(publication.Error);
        provider.VerifyNoOtherCalls();
        Assert.IsType<ConflictObjectResult>((await RappiCatalogTestData.Controller(db, provider.Object).PublishMenu(1, default)).Result);
    }

    [Fact]
    public async Task Late_poll_for_completed_publication_cannot_approve_next_one()
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        var old = RappiCatalogTestData.Publication(1, RappiCatalogTestData.Now.AddHours(-1));
        old.Status = "approved";
        var newer = RappiCatalogTestData.Publication(2, RappiCatalogTestData.Now);
        db.RappiMenuPublications.AddRange(old, newer);
        await db.SaveChangesAsync();
        await RappiCatalogTestData.ApplyMenuAsync(db, "MENU_APPROVED", $"MENU_APPROVED_POLL:{old.Id}");
        Assert.Equal("submitted", newer.Status);
    }

    [Fact]
    public async Task Poll_does_not_guess_between_legacy_parallel_publications()
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        db.RappiMenuPublications.AddRange(RappiCatalogTestData.Publication(1, RappiCatalogTestData.Now.AddHours(-1)),
            RappiCatalogTestData.Publication(2, RappiCatalogTestData.Now));
        await db.SaveChangesAsync();
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        await RappiCatalogTestData.WorkerAsync("RecoverMenuApprovalsAsync", db, provider.Object);
        provider.VerifyNoOtherCalls();
        Assert.Empty(await db.IntegrationWebhookEvents.ToListAsync());
        Assert.All(await db.RappiMenuPublications.ToListAsync(), p => { Assert.Equal("submitted", p.Status); Assert.NotNull(p.Error); });
    }

    [Fact]
    public async Task Poll_enqueues_a_trusted_publication_key_and_approval_persists_that_snapshot()
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        var publication = RappiCatalogTestData.Publication(1, RappiCatalogTestData.Now);
        publication.PayloadJson = """{"items":[{"sku":"product-26","name":"Approved name","description":"Approved description","price":19000}]}""";
        db.RappiMenuPublications.Add(publication);
        await db.SaveChangesAsync();
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.Setup(x => x.GetMenuApprovalAsync("900173116", It.IsAny<CancellationToken>())).ReturnsAsync(new RappiOperationResult(true, 200));
        await RappiCatalogTestData.WorkerAsync("RecoverMenuApprovalsAsync", db, provider.Object);
        var approval = await db.IntegrationWebhookEvents.SingleAsync();
        Assert.Equal($"MENU_APPROVED_POLL:{publication.Id}", approval.EventKey);
        await RappiCatalogTestData.ApplyMenuAsync(db, approval.EventType, approval.EventKey);
        var mapping = await db.DeliveryAppProductMappings.SingleAsync();
        Assert.Equal(19000, mapping.PublishedPrice);
        Assert.Equal("Approved name", mapping.PublishedName);
    }

    [Theory]
    [InlineData("submitting")]
    [InlineData("submitted")]
    [InlineData("approval_review_required")]
    public async Task Pending_publication_blocks_another_provider_send(string status)
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        var publication = RappiCatalogTestData.Publication(1, RappiCatalogTestData.Now);
        publication.Status = status;
        db.RappiMenuPublications.Add(publication);
        await db.SaveChangesAsync();
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        var result = await RappiCatalogTestData.Controller(db, provider.Object).PublishMenu(1, default);
        Assert.IsType<ConflictObjectResult>(result.Result);
        provider.VerifyNoOtherCalls();
        Assert.Single(await db.RappiMenuPublications.ToListAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(408)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Uncertain_menu_send_remains_blocked_for_manual_review(int? statusCode)
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.Setup(x => x.PublishMenuAsync(It.IsAny<RappiMenuRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RappiOperationResult(false, statusCode, "synthetic timeout"));
        Assert.IsType<BadRequestObjectResult>((await RappiCatalogTestData.Controller(db, provider.Object).PublishMenu(1, default)).Result);
        var publication = await db.RappiMenuPublications.SingleAsync();
        Assert.Equal("submitting", publication.Status);
        Assert.NotNull(publication.Error);
        Assert.IsType<ConflictObjectResult>((await RappiCatalogTestData.Controller(db, provider.Object).PublishMenu(1, default)).Result);
        provider.Verify(x => x.PublishMenuAsync(It.IsAny<RappiMenuRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Parent_replacement_cannot_leave_an_omitted_historical_parent()
    {
        var connection = new DeliveryAppConnection();
        var existing = new DeliveryAppStore { RappiStoreId = "old", Name = "Old", IsParent = true };
        connection.Stores.Add(existing);
        var error = RappiCatalogTestData.UpsertStores(connection,
            [new() { RappiStoreId = "new", Name = "New", IsParent = true }]);
        Assert.NotNull(error);
        Assert.Same(existing, Assert.Single(connection.Stores));
        Assert.True(existing.IsParent);
    }

    [Fact]
    public void Normalized_store_ids_match_existing_rows_and_duplicates_are_rejected_without_mutation()
    {
        var connection = new DeliveryAppConnection();
        var existing = new DeliveryAppStore { RappiStoreId = "parent", Name = "Old", IsParent = true };
        connection.Stores.Add(existing);
        Assert.Null(RappiCatalogTestData.UpsertStores(connection,
            [new() { RappiStoreId = " parent ", StoreIntegrationId = " external ", Name = "New", IsParent = true }]));
        Assert.Same(existing, Assert.Single(connection.Stores));
        Assert.Equal("external", existing.StoreIntegrationId);
        Assert.NotNull(RappiCatalogTestData.UpsertStores(connection,
            [new() { RappiStoreId = "parent", Name = "Changed", IsParent = true },
             new() { RappiStoreId = " parent ", Name = "Duplicate", IsParent = false }]));
        Assert.Single(connection.Stores);
        Assert.Equal("New", existing.Name);
    }

    [Theory]
    [InlineData("shared-external")]
    [InlineData("parent")]
    public void Final_stores_cannot_share_an_integration_id_or_cross_alias_another_store(string childIntegrationId)
    {
        var connection = new DeliveryAppConnection();
        connection.Stores.Add(new DeliveryAppStore { RappiStoreId = "parent", StoreIntegrationId = "shared-external", Name = "Parent", IsParent = true });
        var error = RappiCatalogTestData.UpsertStores(connection,
            [new() { RappiStoreId = "parent", StoreIntegrationId = "shared-external", Name = "Parent", IsParent = true },
             new() { RappiStoreId = "child", StoreIntegrationId = childIntegrationId, Name = "Child", IsParent = false }]);
        Assert.NotNull(error);
        Assert.Single(connection.Stores);
    }

    [Fact]
    public void Explicit_parent_change_preserves_both_store_rows_and_empty_update_keeps_configuration()
    {
        var connection = new DeliveryAppConnection();
        connection.Stores.Add(new DeliveryAppStore { RappiStoreId = "old", IsParent = true });
        Assert.Null(RappiCatalogTestData.UpsertStores(connection,
            [new() { RappiStoreId = "old", Name = "Old", IsParent = false },
             new() { RappiStoreId = "new", Name = "New", IsParent = true }]));
        Assert.Equal(2, connection.Stores.Count);
        Assert.Equal("new", Assert.Single(connection.Stores, x => x.IsParent).RappiStoreId);
        Assert.Null(RappiCatalogTestData.UpsertStores(connection, []));
        Assert.Equal(2, connection.Stores.Count);
    }

    [Theory]
    [InlineData("name", true)]
    [InlineData("ready-option", true)]
    [InlineData("whitespace", true)]
    [InlineData("integration-id", false)]
    [InlineData("new-store", false)]
    public async Task Only_routing_identity_changes_require_reverification(string change, bool remainsVerified)
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        var connection = await db.DeliveryAppConnections.SingleAsync();
        connection.LastVerifiedAt = RappiCatalogTestData.Now;
        await db.SaveChangesAsync();
        var dto = RappiCatalogTestData.ConnectionDto();
        if (change == "name") dto.Stores[0].Name = "Renamed";
        if (change == "ready-option") dto.Stores[0].ManualReadyForPickupEnabled = true;
        if (change == "whitespace") { dto.Stores[0].RappiStoreId = " 900173116 "; dto.Stores[0].StoreIntegrationId = " 900173116 "; }
        if (change == "integration-id") dto.Stores[0].StoreIntegrationId = "new-integration";
        if (change == "new-store") dto.Stores.Add(new() { RappiStoreId = "new-child", Name = "Child", IsParent = false });
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.SetupGet(x => x.CredentialsConfigured).Returns(false);
        Assert.IsType<OkObjectResult>((await RappiCatalogTestData.Controller(db, provider.Object).Upsert(1, dto, default)).Result);
        Assert.Equal(remainsVerified, connection.IsVerified);
        if (remainsVerified) Assert.Equal(RappiCatalogTestData.Now, connection.LastVerifiedAt);
        else Assert.Null(connection.LastVerifiedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Store_identifier_cannot_be_assigned_to_another_connection_in_the_same_tenant(bool integrationId)
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        var other = new DeliveryAppConnection { Id = 2, Branch = new Branch { Id = 2, Name = "Other synthetic branch" }, Provider = "rappi", FinancialAppId = 2 };
        other.Stores.Add(new DeliveryAppStore { Id = 2, RappiStoreId = "other-store", StoreIntegrationId = "other-integration", IsParent = true, Name = "Other store" });
        db.DeliveryAppConnections.Add(other);
        await db.SaveChangesAsync();
        var dto = RappiCatalogTestData.ConnectionDto();
        if (integrationId) dto.Stores[0].StoreIntegrationId = " other-integration ";
        else
        {
            dto.Stores[0].IsParent = false;
            dto.Stores.Add(new() { RappiStoreId = " other-store ", Name = "Duplicate", IsParent = true });
        }
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        var result = await RappiCatalogTestData.Controller(db, provider.Object).Upsert(1, dto, default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
        var original = await db.DeliveryAppStores.Where(x => x.ConnectionId == 1).SingleAsync();
        Assert.True(original.IsParent);
        Assert.Equal("900173116", original.StoreIntegrationId);
        provider.VerifyNoOtherCalls();
    }

    // Synthetic fixtures derived from the public menu contract, not a live store publication:
    // https://dev-portal.rappi.com/en/managing-store-menus/ (parent store, gross price, SKU, image URL).
    [Theory]
    [InlineData(false, 18000)]
    [InlineData(true, 19000)]
    public async Task Catalog_build_sends_only_parent_menu_with_stable_SKUs_and_configured_full_price(bool overridePrice, int expectedPrice)
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        var mapping = await db.DeliveryAppProductMappings.SingleAsync();
        mapping.OverridePrice = overridePrice ? 19000 : null;
        mapping.PublishedPrice = 17000; // Historical snapshot is not the current catalog price.
        mapping.OverrideName = "Nombre visible";
        db.DeliveryAppStores.Add(new DeliveryAppStore
        { Id = 2, ConnectionId = 1, RappiStoreId = "child-store", StoreIntegrationId = "child-external", Name = "Child", IsParent = false });
        await db.SaveChangesAsync();
        RappiMenuRequest? sent = null;
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.Setup(x => x.PublishMenuAsync(It.IsAny<RappiMenuRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RappiMenuRequest, CancellationToken>((menu, _) => sent = menu)
            .ReturnsAsync(new RappiOperationResult(true, 200));
        Assert.IsType<OkObjectResult>((await RappiCatalogTestData.Controller(db, provider.Object).PublishMenu(1, default)).Result);
        Assert.NotNull(sent);
        Assert.Equal("900173116", sent.StoreId);
        var item = Assert.Single(sent.Items);
        Assert.Equal(expectedPrice, item.Price);
        Assert.Equal("product-26", item.Sku);
        Assert.Equal("category-1", item.Category.Id);
        Assert.Equal("Nombre visible", item.Name);
        Assert.Equal("Synthetic test description", item.Description);
        Assert.Equal("PRODUCT", item.Type);
        Assert.Empty(item.Children);
        Assert.Equal(0, item.Category.MinQty);
        Assert.Equal(0, item.Category.MaxQty);
        Assert.Equal(0, item.Category.SortingPosition);
        Assert.Equal(0, item.SortingPosition);
        provider.Verify(x => x.PublishMenuAsync(It.IsAny<RappiMenuRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // Official image_url_complies forbids literal whitespace. The DEV portal validator
    // independently rejected the space variant and accepted %20 on 2026-10-21; no menu was sent.
    [Theory]
    [InlineData(" ", false)]
    [InlineData("https://example.test/rice bowl.jpg", false)]
    [InlineData("https://example.test/rice\tbowl.jpg", false)]
    [InlineData("https://example.test/rice\nbowl.jpg", false)]
    [InlineData("https://example.test/rice%20bowl.jpg", true)]
    [InlineData("https://example.test/rice.jpg", true)]
    public async Task Menu_preflight_distinguishes_encoded_spaces_from_literal_URL_whitespace(string imageUrl, bool valid)
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        (await db.DeliveryAppProductMappings.SingleAsync()).OverrideImageUrl = imageUrl;
        await db.SaveChangesAsync();
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        var controller = RappiCatalogTestData.Controller(db, provider.Object);
        var preview = await controller.PreviewMenu(1, default);
        if (valid)
        {
            var response = Assert.IsType<ApiResponse<object>>(Assert.IsType<OkObjectResult>(preview.Result).Value);
            var menu = Assert.IsType<RappiMenuRequest>(response.Data);
            Assert.Equal(imageUrl, Assert.Single(menu.Items).ImageUrl);
        }
        else
        {
            Assert.IsType<BadRequestObjectResult>(preview.Result);
            Assert.IsType<BadRequestObjectResult>((await controller.PublishMenu(1, default)).Result);
            Assert.Empty(await db.RappiMenuPublications.ToListAsync());
        }
        provider.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Delivery_Growth_payment_uses_authoritative_restaurant_total_without_adding_fees_or_courier_tip()
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        var payload = JsonNode.Parse(RappiCatalogTestData.ValidOrder)!;
        var detail = payload["order_detail"]!;
        detail["totals"]!["total_order"] = 17000;
        detail["totals"]!["total_discounts"] = 6000;
        detail["totals"]!["charges"] = new JsonObject { ["shipping"] = 3000, ["service_fee"] = 500 };
        detail["totals"]!["other_totals"] = new JsonObject { ["tip"] = 2000 };
        detail["discounts"]!.AsArray().Add(JsonSerializer.SerializeToNode(new
        { title = "Envío financiado por Rappi", type = "shipping", value = 3000, amount_by_rappi = 3000, amount_by_partner = 0 }));
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.Setup(x => x.AcceptOrderAsync("rappi-order-1", 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RappiOperationResult(true, 200));
        var result = await RappiCatalogTestData.Processor(db, provider.Object).IngestNewOrderAsync(1, payload.ToJsonString(), default);
        Assert.True(result.Success, result.Error);
        var order = await db.Orders.SingleAsync();
        Assert.Equal(18000, order.Subtotal);
        Assert.Equal(17000, order.Total);
        Assert.Equal(3500, order.ExternalCharges);
        Assert.Equal(1000, order.ExternalDiscountByPartner);
        Assert.Equal(5000, order.ExternalDiscountByRappi);
        var payment = await db.AppPayments.SingleAsync();
        Assert.Equal(17000, payment.Amount);
        Assert.Equal(4250m, payment.EstimatedCommissionAmount);
        Assert.Equal(12750m, payment.ExpectedNetAmount);
    }

    [Fact]
    public async Task Documented_TOPPING_shape_is_held_without_silent_loss_or_business_creation_in_simple_product_sprint()
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        var payload = JsonNode.Parse(RappiCatalogTestData.ValidOrder)!;
        var detail = payload["order_detail"]!;
        detail["totals"]!["total_products"] = 21500;
        detail["totals"]!["total_order"] = 21500;
        detail["totals"]!["total_discounts"] = 0;
        detail["totals"]!["total_discount_by_partner"] = 0;
        detail["discounts"] = new JsonArray();
        detail["items"]![0]!["subitems"] = new JsonArray(JsonSerializer.SerializeToNode(new
        { id = "synthetic-topping", sku = "topping-cheese", name = "Queso", type = "TOPPING", price = 3500, quantity = 1 }));
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        var result = await RappiCatalogTestData.Processor(db, provider.Object).IngestNewOrderAsync(1, payload.ToJsonString(), default);
        Assert.True(result.Held);
        Assert.False(result.Success);
        Assert.Contains("modificadores no soportados", result.Error);
        var external = await db.ExternalDeliveryOrders.SingleAsync();
        Assert.Contains("topping-cheese", external.LinesJson);
        Assert.Contains("topping-cheese", external.RawPayloadJson);
        Assert.Empty(await db.Orders.ToListAsync());
        Assert.Empty(await db.AppPayments.ToListAsync());
        Assert.Empty(await db.InventoryMovements.ToListAsync());
        provider.VerifyNoOtherCalls();
    }

    // Official orders contract: price/total_products are gross; production shared funding
    // is split into entries, discounted unit fields are informational, partner total includes coupons.
    // https://dev-portal.rappi.com/en/api-reference/orders/
    // https://dev-portal.rappi.com/en/api-reference/sandbox/
    [Theory]
    [InlineData(1, 0, 2000, 0)]
    [InlineData(1, 1000, 0, 0)]
    [InlineData(2, 2000, 4000, 0)]
    [InlineData(2, 2000, 4000, 1000)]
    public async Task Production_shaped_growth_preserves_gross_price_funding_quantities_and_coupon_total(
        int quantity, int partnerCampaign, int rappiCampaign, int partnerCoupon)
    {
        await using var db = RappiCatalogTestData.NewDb();
        await RappiCatalogTestData.SeedAsync(db);
        var payload = JsonNode.Parse(RappiCatalogTestData.ValidOrder)!;
        var detail = payload["order_detail"]!;
        var gross = 18000 * quantity;
        var partnerTotal = partnerCampaign + partnerCoupon;
        var totals = detail["totals"]!;
        totals["total_products"] = gross;
        totals["total_order"] = gross - partnerTotal;
        totals["total_discounts"] = partnerTotal + rappiCampaign;
        totals["total_discount_by_partner"] = partnerTotal;
        totals["charges"]!["shipping"] = 0;
        var line = detail["items"]![0]!;
        line["quantity"] = quantity;
        line["price"] = 18000;
        line["unit_price_without_discount"] = 18000;
        line["unit_price_with_discount"] = 18000 - partnerCampaign / quantity;
        var discounts = new JsonArray();
        if (partnerCampaign > 0) discounts.Add(JsonSerializer.SerializeToNode(new { title = "Partner campaign", type = "campaign", sku = "product-26", value = partnerCampaign, amount_by_partner = partnerCampaign, amount_by_rappi = 0 }));
        if (rappiCampaign > 0) discounts.Add(JsonSerializer.SerializeToNode(new { title = "Rappi campaign", type = "campaign", sku = "product-26", value = rappiCampaign, amount_by_partner = 0, amount_by_rappi = rappiCampaign }));
        detail["discounts"] = discounts;
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.Setup(x => x.AcceptOrderAsync("rappi-order-1", 25, It.IsAny<CancellationToken>())).ReturnsAsync(new RappiOperationResult(true, 200));
        var processor = RappiCatalogTestData.Processor(db, provider.Object);
        var accepted = await processor.IngestNewOrderAsync(1, payload.ToJsonString(), default);
        Assert.True(accepted.Success, accepted.Error);
        var order = await db.Orders.Include(x => x.OrderDetails).SingleAsync();
        Assert.Equal(gross, order.Subtotal);
        Assert.Equal(gross - partnerTotal, order.Total);
        Assert.Equal(partnerTotal, order.ExternalDiscountByPartner);
        Assert.Equal(rappiCampaign, order.ExternalDiscountByRappi);
        Assert.Equal(quantity, Assert.Single(order.OrderDetails).Quantity);
        var payment = await db.AppPayments.SingleAsync();
        Assert.Equal(order.Total, payment.Amount);
        Assert.Equal(order.Total * 0.75m, payment.ExpectedNetAmount);
        Assert.True((await processor.IngestNewOrderAsync(1, payload.ToJsonString(), default)).Success);
        provider.Verify(x => x.AcceptOrderAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

public sealed class RappiCatalogReadinessPostgreSqlTests : RappiReadyRecoveryDatabase
{
    [PostgreSqlIntegrationFact]
    public async Task Availability_SQL_sends_both_stores_once_and_rechecks_disable_before_queued_retry()
    {
        await using var db = Db();
        await RappiCatalogTestData.SeedRelationalAsync(db);
        db.DeliveryAppStores.Add(new DeliveryAppStore
        { Id = 2, ConnectionId = 1, RappiStoreId = "child", StoreIntegrationId = "child-external", Name = "Child" });
        db.RappiAvailabilityStates.AddRange(RappiCatalogTestData.Availability(), RappiCatalogTestData.Availability(2, false));
        await db.SaveChangesAsync();
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.Setup(x => x.SetAvailabilityAsync(It.IsAny<IReadOnlyCollection<RappiAvailabilityRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RappiOperationResult(true, 200));
        await RappiCatalogTestData.WorkerAsync("ProcessAvailabilityAsync", db, provider.Object);
        await RappiCatalogTestData.WorkerAsync("ProcessAvailabilityAsync", db, provider.Object);
        db.ChangeTracker.Clear();
        var rows = await db.RappiAvailabilityStates.OrderBy(x => x.StoreId).ToListAsync();
        Assert.All(rows, row => { Assert.Equal("synced", row.Status); Assert.Equal(1, row.AttemptCount); Assert.Null(row.NextAttemptAt); });
        Assert.True(rows[0].LastSyncedAvailable);
        Assert.False(rows[1].LastSyncedAvailable);
        var connection = await db.DeliveryAppConnections.SingleAsync();
        Assert.Equal(RappiCatalogTestData.Now, connection.LastAvailabilitySyncAt);
        connection.IsActive = false;
        foreach (var row in rows) row.Status = "pending";
        await db.SaveChangesAsync();
        await RappiCatalogTestData.WorkerAsync("ProcessAvailabilityAsync", db, provider.Object);
        provider.Verify(x => x.SetAvailabilityAsync(It.Is<IReadOnlyCollection<RappiAvailabilityRequest>>(r =>
            r.Count == 2 && r.Any(x => x.StoreIntegrationId == "900173116" && x.TurnOn.Contains("product-26"))
            && r.Any(x => x.StoreIntegrationId == "child-external" && x.TurnOff.Contains("product-26"))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [PostgreSqlIntegrationFact]
    public async Task Menu_response_cannot_overwrite_a_rejection_hold_received_while_HTTP_is_in_flight()
    {
        await using var first = Db();
        await RappiCatalogTestData.SeedRelationalAsync(first);
        await using var callback = Db();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.Setup(x => x.PublishMenuAsync(It.IsAny<RappiMenuRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (RappiMenuRequest _, CancellationToken ct) =>
            {
                Assert.Null(first.Database.CurrentTransaction);
                entered.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
                return new RappiOperationResult(true, 200);
            });
        var sending = RappiCatalogTestData.Controller(first, provider.Object).PublishMenu(1, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        try { await RappiCatalogTestData.ApplyMenuAsync(callback, "MENU_REJECTED", "MENU_REJECTED:123:hash"); }
        finally { release.TrySetResult(); }
        Assert.IsType<ConflictObjectResult>((await sending).Result);
        await using var check = Db();
        Assert.Equal("approval_review_required", (await check.RappiMenuPublications.SingleAsync()).Status);
        Assert.NotNull((await check.DeliveryAppConnections.SingleAsync()).LastError);
        provider.Verify(x => x.PublishMenuAsync(It.IsAny<RappiMenuRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [PostgreSqlIntegrationFact]
    public async Task Stale_poll_completion_rolls_back_approval_snapshots_and_diagnostics_after_rejection_hold()
    {
        await using var poll = Db();
        await RappiCatalogTestData.SeedRelationalAsync(poll);
        var publication = RappiCatalogTestData.Publication(1, RappiCatalogTestData.Now);
        publication.PayloadJson = """{"items":[{"sku":"product-26","name":"Stale approved name","price":19000}]}""";
        poll.RappiMenuPublications.Add(publication);
        await poll.SaveChangesAsync();
        await RappiCatalogTestData.ApplyMenuAsync(poll, "MENU_APPROVED", "MENU_APPROVED_POLL:1", save: false);
        await using var callback = Db();
        await RappiCatalogTestData.ApplyMenuAsync(callback, "MENU_REJECTED", "MENU_REJECTED:123:hash");
        await using (var completion = await poll.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => poll.SaveChangesAsync());
            await completion.RollbackAsync();
        }
        await using var check = Db();
        Assert.Equal("approval_review_required", (await check.RappiMenuPublications.SingleAsync()).Status);
        Assert.Equal(18000, (await check.DeliveryAppProductMappings.SingleAsync()).PublishedPrice);
        Assert.NotNull((await check.DeliveryAppConnections.SingleAsync()).LastError);
    }

    [PostgreSqlIntegrationFact]
    public async Task Concurrent_connections_in_one_tenant_cannot_claim_the_same_provider_store()
    {
        await using var first = Db();
        await RappiCatalogTestData.SeedRelationalAsync(first);
        var branch = new Branch { Id = 2, Name = "Second synthetic branch" };
        var bank = new Bank { Id = 3, Branch = branch, Name = "Second bank" };
        var app = new App { Id = 3, Bank = bank, Name = "Second Rappi" };
        var customer = new Customer { Id = 30000, Branch = branch, Name = "Second internal customer" };
        var user = new User { Id = 24, Branch = branch, Name = "Second technical user", Email = "second@example.test" };
        first.Apps.Add(app); first.Customers.Add(customer); first.Users.Add(user);
        await first.SaveChangesAsync();
        await using var second = Db();
        var a = RappiCatalogTestData.ConnectionDto();
        a.Stores[0].IsParent = false;
        a.Stores.Add(new() { RappiStoreId = "shared-new-store", Name = "Shared", IsParent = true });
        var b = new UpsertRappiConnectionDto
        {
            FinancialAppId = 3, CustomerId = 30000, TechnicalUserId = 24, IsActive = true,
            Stores = [new() { RappiStoreId = "shared-new-store", Name = "Shared", IsParent = true }]
        };
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.SetupGet(x => x.CredentialsConfigured).Returns(false);
        var results = await Task.WhenAll(
            RappiCatalogTestData.Controller(first, provider.Object).Upsert(1, a, default),
            RappiCatalogTestData.Controller(second, provider.Object, branchId: 2).Upsert(2, b, default));
        Assert.Single(results, x => x.Result is OkObjectResult);
        Assert.Single(results, x => x.Result is BadRequestObjectResult);
        await using var check = Db();
        Assert.Single(await check.DeliveryAppStores.Where(x => x.RappiStoreId == "shared-new-store").ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task Concurrent_store_updates_validate_the_final_collection_under_the_connection_lock()
    {
        await using var first = Db();
        await RappiCatalogTestData.SeedRelationalAsync(first);
        await using var second = Db();
        var a = RappiCatalogTestData.ConnectionDto();
        var b = RappiCatalogTestData.ConnectionDto();
        a.Stores[0].IsParent = b.Stores[0].IsParent = false;
        a.Stores.Add(new() { RappiStoreId = "new-parent-a", Name = "A", IsParent = true });
        b.Stores.Add(new() { RappiStoreId = "new-parent-b", Name = "B", IsParent = true });
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.SetupGet(x => x.CredentialsConfigured).Returns(false);
        var results = await Task.WhenAll(
            RappiCatalogTestData.Controller(first, provider.Object).Upsert(1, a, default),
            RappiCatalogTestData.Controller(second, provider.Object).Upsert(1, b, default));
        Assert.Single(results, x => x.Result is OkObjectResult);
        Assert.Single(results, x => x.Result is BadRequestObjectResult);
        await using var check = Db();
        var stores = await check.DeliveryAppStores.ToListAsync();
        Assert.Equal(2, stores.Count);
        Assert.Single(stores, x => x.IsParent);
        Assert.Contains(stores, x => x.RappiStoreId == "900173116");
    }

    [PostgreSqlIntegrationFact]
    public async Task Concurrent_publish_reserves_one_pending_publication_before_any_provider_response()
    {
        await using var first = Db();
        await RappiCatalogTestData.SeedRelationalAsync(first);
        await using var second = Db();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Mock<IRappiDeliveryProvider>(MockBehavior.Strict);
        provider.Setup(x => x.PublishMenuAsync(It.IsAny<RappiMenuRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (RappiMenuRequest _, CancellationToken ct) => { Assert.Null(first.Database.CurrentTransaction); entered.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(20), ct); return new RappiOperationResult(true, 200); });
        var sending = RappiCatalogTestData.Controller(first, provider.Object).PublishMenu(1, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            var duplicate = await RappiCatalogTestData.Controller(second, provider.Object).PublishMenu(1, default).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsType<ConflictObjectResult>(duplicate.Result);
        }
        finally { release.TrySetResult(); }
        Assert.IsType<OkObjectResult>((await sending).Result);
        await using var check = Db();
        Assert.Equal("submitted", (await check.RappiMenuPublications.SingleAsync()).Status);
        provider.Verify(x => x.PublishMenuAsync(It.IsAny<RappiMenuRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

internal static class RappiCatalogTestData
{
    internal static readonly DateTime Now = new(2026, 10, 21, 17, 0, 0, DateTimeKind.Utc);
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    internal static string ValidOrder => (string)typeof(RappiOrderProcessorTests).GetField("ValidOrder", PrivateStatic)!.GetRawConstantValue()!;
    internal static ApplicationDbContext NewDb() => (ApplicationDbContext)typeof(RappiOrderProcessorTests).GetMethod("CreateDb", PrivateStatic)!.Invoke(null, null)!;
    internal static async Task SeedAsync(ApplicationDbContext db)
    {
        await (Task)typeof(RappiOrderProcessorTests).GetMethod("SeedAsync", PrivateStatic)!.Invoke(null, [db])!;
        (await db.DeliveryAppProductMappings.SingleAsync()).OverrideDescription = "Synthetic test description";
        await db.SaveChangesAsync();
    }
    internal static async Task SeedRelationalAsync(ApplicationDbContext db)
    {
        db.Tenants.Add(new Tenant { Id = 1, Name = "Synthetic catalog test", Slug = "catalog-test" });
        await db.SaveChangesAsync();
        await SeedAsync(db);
        // The reused seed supplies explicit IDs; advance only this disposable fixture's identities.
        await db.Database.ExecuteSqlRawAsync("SELECT setval(pg_get_serial_sequence('delivery_app_store', 'id'), (SELECT MAX(id) FROM delivery_app_store)); SELECT setval(pg_get_serial_sequence('delivery_app_connection', 'id'), (SELECT MAX(id) FROM delivery_app_connection));");
    }
    internal static RappiOrderProcessor Processor(ApplicationDbContext db, IRappiDeliveryProvider provider) =>
        (RappiOrderProcessor)typeof(RappiOrderProcessorTests).GetMethod("CreateProcessor", PrivateStatic)!.Invoke(null, [db, provider, null, null, null, null, null])!;
    internal static Task WorkerAsync(string method, ApplicationDbContext db, IRappiDeliveryProvider provider, string? environment = null) =>
        (Task)typeof(RappiIntegrationWorker).GetMethod(method, PrivateStatic)!.Invoke(null, [db, provider, new FakeClock(Now), CancellationToken.None, environment])!;
    internal static async Task ApplyMenuAsync(ApplicationDbContext db, string eventType, string eventKey, bool save = true)
    {
        var method = typeof(RappiOrderProcessor).GetMethod("ApplyMenuStatusAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var args = method.GetParameters().Length == 4
            ? new object[] { 1, eventType, "{}", CancellationToken.None }
            : new object[] { 1, eventType, "{}", CancellationToken.None, eventKey };
        await (Task)method.Invoke(Processor(db, Mock.Of<IRappiDeliveryProvider>(MockBehavior.Strict)), args)!;
        if (save) await db.SaveChangesAsync();
    }
    internal static string? UpsertStores(DeliveryAppConnection connection, UpsertRappiStoreDto[] input) =>
        (string?)typeof(RappiIntegrationsController).GetMethod("UpsertStores", PrivateStatic)!.Invoke(null, [connection, input]);
    internal static RappiAvailabilityState Availability(int storeId = 1, bool desired = true) => new()
    { ConnectionId = 1, StoreId = storeId, ProductMappingId = 1, DesiredAvailable = desired, Status = "pending", CreatedAt = Now, UpdatedAt = Now };
    internal static RappiMenuPublication Publication(int id, DateTime created) => new()
    { Id = id, ConnectionId = 1, StoreId = "900173116", PayloadJson = "{\"items\":[]}", Status = "submitted", CreatedAt = created, UpdatedAt = created };
    internal static UpsertRappiConnectionDto ConnectionDto() => new()
    {
        FinancialAppId = 2, CustomerId = 25714, TechnicalUserId = 23, IsActive = true,
        Stores = [new() { RappiStoreId = "900173116", StoreIntegrationId = "900173116", Name = "Parent", IsParent = true }]
    };
    internal static RappiIntegrationsController Controller(ApplicationDbContext db, IRappiDeliveryProvider provider, int branchId = 1)
    {
        var actor = new Mock<ICurrentUser>(); actor.SetupGet(x => x.Role).Returns("Admin"); actor.SetupGet(x => x.BranchId).Returns(branchId);
        return new(db, actor.Object, new FakeClock(Now), Mock.Of<IIntegrationSecretProtector>(), provider, Mock.Of<IRappiOrderProcessor>(),
            Options.Create(new ApiPublicOptions()), Mock.Of<IBackgroundWorkSignal<RappiWork>>(), new TestTenantContext());
    }
}
