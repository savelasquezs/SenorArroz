using System.Text.Json;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
namespace SenorArroz.Application.Common.Services;
public class AgentToolExecutor(IEnumerable<IAgentTool> tools) : IAgentToolExecutor
{
    private readonly Dictionary<string, IAgentTool> _tools = tools.ToDictionary(x=>x.Name,StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<AiToolDefinition> Definitions => _tools.Values.Select(x=>new AiToolDefinition(x.Name,x.Description,x.ParametersSchema)).ToList();
    public Task<AgentToolExecutionResult> ExecuteAsync(string name, AgentToolExecutionContext context, JsonElement arguments, CancellationToken cancellationToken) => _tools.TryGetValue(name,out var tool) ? tool.ExecuteAsync(context,arguments,cancellationToken) : Task.FromResult(new AgentToolExecutionResult(false,null,"Herramienta no permitida."));
}
