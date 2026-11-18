using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public sealed partial class WhatsAppProcessingPostgreSqlTests
{
    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Stage45_Concurrent_confirmations_return_one_remote_order_and_one_outbox_message()
    {
        using var tenant = _tenant.BeginTenantScope(1);
        await using var seed = Db();
        await SeedManualAsync(seed);
        seed.ProductCategories.Add(new ProductCategory { Id = 30, BranchId = 2, Name = "Arroces" });
        seed.Products.Add(new Product { Id = 31, CategoryId = 30, Name = "Arroz", Price = 10000 });
        await seed.SaveChangesAsync();
        var controller = Stage23Controller(seed, 7);
        var started = CommercialPayload(await controller.StartCommercialDraft(11, new(), default));
        var saved = CommercialPayload(await controller.SaveCommercialDraft(11, new()
        {
            SessionId = started.SessionId, ExpectedVersion = started.Version,
            Order = new() { BranchId = 2, Type = OrderType.Onsite, GuestName = "Recoge remoto",
                OrderDetails = [new() { ProductId = 31, Quantity = 2, UnitPrice = 10000 }] }
        }, default));
        var request = new WhatsAppDraftWriteDto { SessionId = saved.SessionId, ExpectedVersion = saved.Version };
        var quote = Assert.IsType<ApiResponse<WhatsAppDraftQuoteDto>>(Assert.IsType<OkObjectResult>(await controller.CommercialDraftAction(11,
            "quote", request, WhatsAppOperationalBranchTests.CommercialSender(seed, _clock).Object, default)).Value).Data!;
        request.ReviewedFingerprint = quote.Fingerprint;
        async Task<WhatsAppCommercialDraftDto> Confirm()
        {
            await using var db = Db();
            return CommercialPayload(await Stage23Controller(db, 7).CommercialDraftAction(11, "confirm", request,
                WhatsAppOperationalBranchTests.CommercialSender(db, _clock).Object, default));
        }
        var results = await Task.WhenAll(Confirm(), Confirm());
        Assert.Equal(results[0].OrderId, results[1].OrderId);
        await using var verify = Db();
        var order = await verify.Orders.SingleAsync(x => x.Id == results[0].OrderId);
        Assert.Equal(2, order.BranchId);
        Assert.Equal(1, order.OriginBranchId);
        Assert.Equal(7, order.TakenById);
        Assert.Equal(2, await verify.Orders.CountAsync());
        Assert.Single(await verify.WhatsAppCommerceOutboxMessages.ToListAsync());
        var conversation = await verify.WhatsAppConversations.SingleAsync();
        Assert.Equal(1, conversation.OperationalBranchId);
        Assert.Equal(7, conversation.AssignedUserId);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Stage45_Concurrent_autosaves_reject_stale_revision_without_lost_update()
    {
        using var tenant = _tenant.BeginTenantScope(1);
        await using var seed = Db(); await SeedManualAsync(seed);
        var started = CommercialPayload(await Stage23Controller(seed, 7).StartCommercialDraft(11, new(), default));
        async Task<IActionResult> Save(string note)
        {
            await using var db = Db();
            return await Stage23Controller(db, 7).SaveCommercialDraft(11, new()
            {
                SessionId = started.SessionId, ExpectedVersion = started.Version,
                Order = new() { BranchId = 2, GuestName = "Recoge", Notes = note, Type = OrderType.Onsite }
            }, default);
        }
        var results = await Task.WhenAll(Save("Primer asesor"), Save("Otra pantalla"));
        Assert.Single(results.OfType<OkObjectResult>());
        Assert.Single(results.OfType<ConflictObjectResult>());
        await using var verify = Db();
        var session = await verify.WhatsAppCommerceSessions.SingleAsync();
        Assert.Equal(started.Version + 1, session.Version);
        Assert.Contains(WhatsAppCommercialDraft.Read(session).Notes, new[] { "Primer asesor", "Otra pantalla" });
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Stage45_Schema_is_idempotent_branch_line_drafts_work_and_tenant_filter_is_preserved()
    {
        using (var tenant = _tenant.BeginTenantScope(1))
        {
            await using var db = Db(); await SeedManualAsync(db);
            var conversation = await db.WhatsAppConversations.SingleAsync();
            conversation.ChannelSettingId = null;
            db.WhatsAppBranchSettings.Add(new WhatsAppBranchSetting { BranchId = 1, PhoneNumberId = "branch-line", IsActive = true, IsVerified = true });
            await db.SaveChangesAsync();
            var script = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "SenorArroz.Infrastructure", "Scripts", "add_whatsapp_shared_draft.sql"));
            await using (var command = db.Database.GetDbConnection().CreateCommand())
            {
                await db.Database.OpenConnectionAsync();
                command.CommandText = script;
                await command.ExecuteNonQueryAsync();
            }
            await using (var command = db.Database.GetDbConnection().CreateCommand())
            {
                await db.Database.OpenConnectionAsync();
                command.CommandText = script;
                await command.ExecuteNonQueryAsync();
            }
            var result = CommercialPayload(await Stage23Controller(db, 7).StartCommercialDraft(11, new(), default));
            Assert.Null((await db.WhatsAppCommerceSessions.SingleAsync()).ChannelSettingId);
            Assert.Equal(11, result.ConversationId);
            var session = await db.WhatsAppCommerceSessions.SingleAsync();
            session.Status = "completed";
            (await db.Orders.SingleAsync()).StorefrontIdempotencyKey = session.IdempotencyKey;
            await db.SaveChangesAsync();
            await using (var command = db.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = script;
                await command.ExecuteNonQueryAsync();
            }
            var restored = await db.WhatsAppCommerceSessions.AsNoTracking().SingleAsync();
            Assert.Equal(91, restored.OrderId);
            Assert.Equal("order_taken", restored.CommercialStatus);
        }
        using (var tenant = _tenant.BeginTenantScope(2))
        {
            await using var other = Db();
            Assert.Empty(await other.WhatsAppCommerceSessions.ToListAsync());
            Assert.IsType<NotFoundResult>(await Stage23Controller(other, 7).GetCommercialDraft(11, default));
        }
    }

    private static WhatsAppCommercialDraftDto CommercialPayload(IActionResult result) =>
        Assert.IsType<ApiResponse<WhatsAppCommercialDraftDto>>(Assert.IsType<OkObjectResult>(result).Value).Data!;
}
