using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public sealed partial class WhatsAppProcessingPostgreSqlTests
{
    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Stage6_Inbox_keyset_orders_by_activity_and_pages_beyond_200_without_secrets_or_n_plus_one()
    {
        using var tenant = _tenant.BeginTenantScope(1);
        await using (var seed = Db())
        {
            await SeedManualAsync(seed);
            var indexScript = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "SenorArroz.Infrastructure", "Scripts", "add_whatsapp_inbox_pagination.sql"));
            await seed.Database.ExecuteSqlRawAsync(indexScript);
            await seed.Database.ExecuteSqlRawAsync(indexScript);
            var activity = _clock.UtcNow.AddHours(-1);
            (await seed.WhatsAppConversations.SingleAsync()).LastMessageAt = activity;
            foreach (var id in Enumerable.Range(20, 261))
                seed.WhatsAppConversations.Add(new WhatsAppConversation
                {
                    Id = id, BranchId = 1, ChannelSettingId = 1, OperationalBranchId = 1, AssignedUserId = 7,
                    ContactName = $"Chat local {id}", LastMessageAt = id == 280 ? activity.AddDays(-10) : activity,
                    NeedsHumanResponse = id == 280, AiOrderState = JsonSerializer.Serialize(new { payload = new string('x', 100000) }), AttentionMode = WhatsAppAttentionMode.Human
                });
            seed.WhatsAppInboxLabels.Add(new WhatsAppInboxLabel { Id = 1, TenantId = 1, Name = "Revisar", NormalizedName = "REVISAR", Color = "violet" });
            seed.WhatsAppConversationLabels.Add(new WhatsAppConversationLabel { TenantId = 1, ConversationId = 280, LabelId = 1 });
            await seed.SaveChangesAsync();
        }
        var commands = new List<string>();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>(_dbOptions)
            .LogTo(sql => commands.Add(sql), [Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted]).Options;
        await using var db = new ApplicationDbContext(options, currentTenant: _tenant, tenantExecutionContext: _tenant);
        var controller = Stage23Controller(db, 7);
        var first = InboxPayload(await controller.GetConversationPage(new() { Inbox = "mine" }, default));
        Assert.Equal(279, first.Items[0].Id);
        Assert.Equal(50, first.Items.Count);
        Assert.Equal(3, commands.Count);
        Assert.DoesNotContain(commands, sql => sql.Contains("ai_order_state") || sql.Contains("access_token") || sql.Contains("state_json"));
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(first).Length < 100000);
        var all = first.Items.Select(x => x.Id).ToList();
        var page = first;
        while (page.HasMore)
        {
            page = InboxPayload(await controller.GetConversationPage(new() { Inbox = "mine", Cursor = page.NextCursor }, default));
            all.AddRange(page.Items.Select(x => x.Id));
        }
        Assert.Equal(262, all.Count);
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Equal(280, all[^1]);
        Assert.Contains("custom:1", page.Items[^1].Labels!);
        Assert.Null(page.NextCursor);
        Assert.IsType<BadRequestObjectResult>((await Stage23Controller(db, 8).GetConversationPage(new() { Inbox = "mine", Cursor = first.NextCursor }, default)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.GetConversationPage(new() { Inbox = "team", Cursor = first.NextCursor }, default)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.GetConversationPage(new() { Limit = 101 }, default)).Result);
        Assert.IsType<ForbidResult>((await controller.GetConversationPage(new() { BranchId = 2 }, default)).Result);
    }

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task Stage6_Inbox_search_matches_formatted_numbers_and_handles_across_accessible_tabs()
    {
        using var tenant = _tenant.BeginTenantScope(1);
        await using var db = Db();
        await SeedManualAsync(db);
        var activity = _clock.UtcNow.AddHours(-1);
        db.WhatsAppConversations.AddRange(
            new WhatsAppConversation { Id = 20, BranchId = 1, ChannelSettingId = 1, OperationalBranchId = 1,
                ContactName = "Consulta libre", LastMessageAt = activity, PhoneNumber = "573005555121", AttentionMode = WhatsAppAttentionMode.Paused },
            new WhatsAppConversation { Id = 21, BranchId = 1, ChannelSettingId = 1, OperationalBranchId = 1,
                ContactName = "Consulta cerrada", LastMessageAt = activity.AddMinutes(-1), WhatsAppUsername = "@consulta_arroz",
                AttentionMode = WhatsAppAttentionMode.Closed, Status = WhatsAppConversationStatus.Closed },
            new WhatsAppConversation { Id = 22, BranchId = 1, ChannelSettingId = 1, OperationalBranchId = 2,
                ContactName = "Consulta otra sede", LastMessageAt = activity.AddMinutes(1), AttentionMode = WhatsAppAttentionMode.Paused });
        await db.SaveChangesAsync();
        var controller = Stage23Controller(db, 7);
        var phone = InboxPayload(await controller.GetConversationPage(new() { Search = "+57 (300) 555-5121" }, default));
        Assert.Equal(20, Assert.Single(phone.Items).Id);
        var handle = InboxPayload(await controller.GetConversationPage(new() { Search = "@consulta_ARROZ" }, default));
        Assert.Equal(21, Assert.Single(handle.Items).Id);
        var name = InboxPayload(await controller.GetConversationPage(new() { Search = "CONSULTA" }, default));
        Assert.Equal(new[] { 20, 21 }, name.Items.Select(x => x.Id));
    }

    private static WhatsAppConversationPageDto InboxPayload(ActionResult<ApiResponse<WhatsAppConversationPageDto>> result) =>
        Assert.IsType<ApiResponse<WhatsAppConversationPageDto>>(Assert.IsType<OkObjectResult>(result.Result).Value).Data!;
}
