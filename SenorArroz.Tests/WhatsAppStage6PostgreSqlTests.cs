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
    public async Task Stage6_Inbox_keyset_preserves_old_pending_items_and_pages_beyond_200_without_secrets_or_n_plus_one()
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
        Assert.Equal(280, first.Items[0].Id);
        Assert.Contains("custom:1", first.Items[0].Labels!);
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
        Assert.Null(page.NextCursor);
        Assert.IsType<BadRequestObjectResult>((await Stage23Controller(db, 8).GetConversationPage(new() { Inbox = "mine", Cursor = first.NextCursor }, default)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.GetConversationPage(new() { Inbox = "team", Cursor = first.NextCursor }, default)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.GetConversationPage(new() { Limit = 101 }, default)).Result);
        Assert.IsType<ForbidResult>((await controller.GetConversationPage(new() { BranchId = 2 }, default)).Result);
    }

    private static WhatsAppConversationPageDto InboxPayload(ActionResult<ApiResponse<WhatsAppConversationPageDto>> result) =>
        Assert.IsType<ApiResponse<WhatsAppConversationPageDto>>(Assert.IsType<OkObjectResult>(result.Result).Value).Data!;
}
