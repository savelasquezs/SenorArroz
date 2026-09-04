using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Services;

public class AgentToolExecutor : IAgentToolExecutor, IAgentToolCatalog
{
    private readonly Dictionary<string, IAgentTool> _registry;
    private readonly IApplicationDbContext _db;

    public AgentToolExecutor(IEnumerable<IAgentTool> tools, IApplicationDbContext db)
        : this(tools, db, new AiToolSchemaValidator()) { }

    public AgentToolExecutor(IEnumerable<IAgentTool> tools, IApplicationDbContext db, IAiToolSchemaValidator schemaValidator)
    {
        _db = db;
        _registry = tools.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        Definitions = _registry.Values.Select(x => new AiToolDefinition(x.Name, x.Description, x.ParametersSchema)).ToList();
        schemaValidator.ValidateOrThrow(Definitions);
    }

    public IReadOnlyList<AiToolDefinition> Definitions { get; }
    public IReadOnlyList<AiToolDefinition> All => Definitions;
    public IReadOnlyList<AiToolDefinition> GetByNames(IEnumerable<string> names)
    {
        var allowed = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Definitions.Where(x => allowed.Contains(x.Name)).ToList();
    }
    public bool ModifiesData(string name) => _registry.TryGetValue(name, out var tool) && tool.ModifiesData;

    public async Task<AgentToolExecutionResult> ExecuteAsync(string name, AgentToolExecutionContext context, JsonElement arguments, CancellationToken ct)
    {
        if (!_registry.TryGetValue(name, out var tool)) return new(false, null, "Herramienta no permitida.", "tool_not_allowed");
        var conversation = await _db.WhatsAppConversations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == context.ConversationId, ct);
        if (conversation is null) return new(false, null, "Conversación no encontrada.", "conversation_not_found");
        var effectiveBranchId = conversation.OperationalBranchId ?? conversation.BranchId;
        if (effectiveBranchId != context.BranchId) return new(false, null, "La conversación no pertenece a la sucursal indicada por el contexto interno.", "branch_mismatch");
        if (conversation.ChannelSettingId.HasValue && !conversation.OperationalBranchId.HasValue
            && !name.Equals("request_human_assistance", StringComparison.OrdinalIgnoreCase))
            return new(false, null, "La conversación central todavía no tiene una sucursal operativa asignada.", "operational_branch_required");
        if (tool.RequiresAiMode && conversation.AttentionMode != WhatsAppAttentionMode.Ai) return new(false, null, "La conversación ya no está siendo atendida por IA.", "attention_mode_changed");
        if (arguments.ValueKind != JsonValueKind.Object) return new(false, null, "Los argumentos deben ser un objeto JSON.", "invalid_arguments");
        var schema = tool.ParametersSchema;
        if (schema.TryGetProperty("required", out var required))
            foreach (var item in required.EnumerateArray())
                if (!arguments.TryGetProperty(item.GetString()!, out _)) return new(false, null, $"Falta el argumento requerido: {item.GetString()}.", "invalid_arguments");
        if (schema.TryGetProperty("properties", out var properties))
            foreach (var property in arguments.EnumerateObject())
                if (!properties.TryGetProperty(property.Name, out _)) return new(false, null, $"Argumento no permitido: {property.Name}.", "invalid_arguments");
        return await tool.ExecuteAsync(context with { BranchId = effectiveBranchId, PhoneNumber = conversation.PhoneNumber, CustomerId = conversation.CustomerId, AttentionMode = conversation.AttentionMode.ToString() }, arguments, ct);
    }
}
