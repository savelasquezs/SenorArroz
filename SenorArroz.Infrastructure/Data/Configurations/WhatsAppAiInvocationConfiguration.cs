using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Infrastructure.Data.Configurations;

public class WhatsAppAiInvocationConfiguration : IEntityTypeConfiguration<WhatsAppAiInvocation>
{
    public void Configure(EntityTypeBuilder<WhatsAppAiInvocation> b)
    {
        b.ToTable("whatsapp_ai_invocation");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.BranchId).HasColumnName("branch_id"); b.Property(x => x.ConversationId).HasColumnName("conversation_id"); b.Property(x => x.IncomingMessageId).HasColumnName("incoming_message_id");
        b.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(40).IsRequired(); b.Property(x => x.Model).HasColumnName("model").HasMaxLength(120).IsRequired();
        b.Property(x => x.InvocationIndex).HasColumnName("invocation_index"); b.Property(x => x.AttemptIndex).HasColumnName("attempt_index");
        b.Property(x => x.StartedAt).HasColumnName("started_at"); b.Property(x => x.CompletedAt).HasColumnName("completed_at"); b.Property(x => x.DurationMs).HasColumnName("duration_ms");
        b.Property(x => x.InputTokens).HasColumnName("input_tokens"); b.Property(x => x.CachedInputTokens).HasColumnName("cached_input_tokens"); b.Property(x => x.OutputTokens).HasColumnName("output_tokens"); b.Property(x => x.ThinkingTokens).HasColumnName("thinking_tokens"); b.Property(x => x.BillableOutputTokens).HasColumnName("billable_output_tokens");
        b.Property(x => x.ToolCallCount).HasColumnName("tool_call_count"); b.Property(x => x.FinishReason).HasColumnName("finish_reason").HasMaxLength(80);
        b.Property(x => x.Success).HasColumnName("success"); b.Property(x => x.IsTransientError).HasColumnName("is_transient_error"); b.Property(x => x.HttpStatusCode).HasColumnName("http_status_code");
        b.Property(x => x.ErrorCategory).HasColumnName("error_category").HasMaxLength(80); b.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(500);
        b.Property(x => x.InputPricePerMillionUsd).HasColumnName("input_price_per_million_usd").HasPrecision(18, 8); b.Property(x => x.CachedInputPricePerMillionUsd).HasColumnName("cached_input_price_per_million_usd").HasPrecision(18, 8); b.Property(x => x.OutputPricePerMillionUsd).HasColumnName("output_price_per_million_usd").HasPrecision(18, 8); b.Property(x => x.EstimatedCostUsd).HasColumnName("estimated_cost_usd").HasPrecision(18, 10); b.Property(x => x.PricingEffectiveDate).HasColumnName("pricing_effective_date");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x=>x.ContextStrategy).HasColumnName("context_strategy").HasMaxLength(32).HasDefaultValue("simple_v1"); b.Property(x=>x.ContextMessageCount).HasColumnName("context_message_count"); b.Property(x=>x.ToolDefinitionCount).HasColumnName("tool_definition_count"); b.Property(x=>x.SystemPromptCharacters).HasColumnName("system_prompt_characters"); b.Property(x=>x.RuntimeContextCharacters).HasColumnName("runtime_context_characters"); b.Property(x=>x.HistoryCharacters).HasColumnName("history_characters"); b.Property(x=>x.ToolDefinitionsCharacters).HasColumnName("tool_definitions_characters"); b.Property(x=>x.ContextPlannerFallback).HasColumnName("context_planner_fallback"); b.Property(x=>x.ContextPlannerFallbackReason).HasColumnName("context_planner_fallback_reason").HasMaxLength(300);
        b.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Conversation).WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.IncomingMessage).WithMany().HasForeignKey(x => x.IncomingMessageId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.BranchId, x.CreatedAt }).HasDatabaseName("idx_whatsapp_ai_invocation_branch_created");
        b.HasIndex(x => new { x.Provider, x.Model, x.CreatedAt }).HasDatabaseName("idx_whatsapp_ai_invocation_provider_model_created");
        b.HasIndex(x => x.IncomingMessageId).HasDatabaseName("idx_whatsapp_ai_invocation_message"); b.HasIndex(x => x.ConversationId).HasDatabaseName("idx_whatsapp_ai_invocation_conversation");
    }
}
