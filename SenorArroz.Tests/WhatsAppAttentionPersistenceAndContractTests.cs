using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public class WhatsAppAttentionPersistenceAndContractTests
{
    [Fact]
    public void AttentionDto_JsonUsesUnifiedAuditProperty()
    {
        var json = JsonSerializer.Serialize(new WhatsAppAttentionDto { ConversationId = 1, AttentionModeUpdatedByUserId = 9 }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"attentionModeUpdatedByUserId\":9", json);
        Assert.DoesNotContain("\"updatedByUserId\"", json);
    }

    [Fact]
    public void ConversationModel_HasOptionalUserForeignKeysWithSetNull()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new ApplicationDbContext(options);
        var entity = db.Model.FindEntityType(typeof(WhatsAppConversation))!;
        var assigned = entity.GetForeignKeys().Single(x => x.Properties.Single().Name == nameof(WhatsAppConversation.AssignedUserId));
        var updatedBy = entity.GetForeignKeys().Single(x => x.Properties.Single().Name == nameof(WhatsAppConversation.AttentionModeUpdatedByUserId));
        Assert.Equal(DeleteBehavior.SetNull, assigned.DeleteBehavior);
        Assert.Equal(DeleteBehavior.SetNull, updatedBy.DeleteBehavior);
        Assert.False(assigned.Properties.Single().IsNullable == false);
        Assert.False(updatedBy.Properties.Single().IsNullable == false);
    }
}
