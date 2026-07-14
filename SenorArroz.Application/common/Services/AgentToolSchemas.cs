using System.Text.Json;

namespace SenorArroz.Application.Common.Services;

public static class AgentToolSchemas
{
    public static JsonElement EmptyObject { get; } = JsonDocument.Parse(
        """{"type":"object","properties":{},"additionalProperties":false}""").RootElement.Clone();
}
