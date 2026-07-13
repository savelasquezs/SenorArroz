using System.Text.Json;
using System.Text.RegularExpressions;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;

namespace SenorArroz.Application.Common.Services;

public sealed partial class AiToolSchemaValidator : IAiToolSchemaValidator
{
    private static readonly string[] ForbiddenRootKeywords = ["anyOf", "oneOf", "allOf", "not", "const", "enum"];

    public AiToolSchemaValidationError? Validate(IReadOnlyList<AiToolDefinition> tools)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            if (string.IsNullOrWhiteSpace(tool.Name) || !ToolNameRegex().IsMatch(tool.Name))
                return Error(tool.Name, "$.name", "debe contener de 1 a 64 letras, números, guiones o guiones bajos");
            if (!names.Add(tool.Name)) return Error(tool.Name, "$.name", "está duplicado en el catálogo");
            var schema = tool.ParametersSchema;
            if (schema.ValueKind != JsonValueKind.Object) return Error(tool.Name, "$", "parameters debe ser un objeto JSON serializable");
            try { _ = schema.GetRawText(); }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            { return Error(tool.Name, "$", "parameters no se puede serializar"); }
            if (!schema.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "object")
                return Error(tool.Name, "$.type", "la raíz debe declarar type=object");
            foreach (var keyword in ForbiddenRootKeywords)
                if (schema.TryGetProperty(keyword, out _)) return Error(tool.Name, $"$.{keyword}", $"'{keyword}' no está permitido en la raíz");
            if (!schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
                return Error(tool.Name, "$.properties", "debe existir y ser un objeto");
            if (schema.TryGetProperty("required", out var required))
            {
                if (required.ValueKind != JsonValueKind.Array) return Error(tool.Name, "$.required", "debe ser un arreglo de nombres");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var index = 0;
                foreach (var item in required.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                        return Error(tool.Name, $"$.required[{index}]", "debe ser un nombre de propiedad no vacío");
                    var name = item.GetString()!;
                    if (!seen.Add(name)) return Error(tool.Name, $"$.required[{index}]", $"'{name}' está duplicado");
                    if (!properties.TryGetProperty(name, out _)) return Error(tool.Name, $"$.required[{index}]", $"'{name}' no existe en properties");
                    index++;
                }
            }
            if (!schema.TryGetProperty("additionalProperties", out var additional) || additional.ValueKind != JsonValueKind.False)
                return Error(tool.Name, "$.additionalProperties", "debe ser false");
        }
        return null;
    }

    public void ValidateOrThrow(IReadOnlyList<AiToolDefinition> tools)
    {
        var error = Validate(tools);
        if (error is not null) throw new InvalidOperationException(error.ToString());
    }

    private static AiToolSchemaValidationError Error(string? tool, string location, string message) =>
        new(string.IsNullOrWhiteSpace(tool) ? "<sin_nombre>" : tool, location, message);

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex ToolNameRegex();
}

public static class AiToolDefinitionValidator
{
    public static (string Name, string Error)? Validate(IReadOnlyList<AiToolDefinition> tools)
    {
        var error = new AiToolSchemaValidator().Validate(tools);
        return error is null ? null : (error.ToolName, $"{error.Location}: {error.Message}");
    }
}
