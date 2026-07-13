using System.Text.Json;
using System.Text.RegularExpressions;
using SenorArroz.Application.Common.Models;

namespace SenorArroz.Application.Common.Services;

public static partial class AiToolDefinitionValidator
{
    public static (string Name, string Error)? Validate(IReadOnlyList<AiToolDefinition> tools)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            if (string.IsNullOrWhiteSpace(tool.Name) || !ToolNameRegex().IsMatch(tool.Name))
                return (tool.Name, "nombre no válido; use 1 a 64 letras, números, guion o guion bajo");
            if (!names.Add(tool.Name))
                return (tool.Name, "nombre duplicado");
            if (tool.ParametersSchema.ValueKind != JsonValueKind.Object)
                return (tool.Name, "parameters debe ser un esquema JSON válido de tipo object");
            if (!tool.ParametersSchema.TryGetProperty("type", out var rootType)
                || rootType.ValueKind != JsonValueKind.String
                || !string.Equals(rootType.GetString(), "object", StringComparison.Ordinal))
                return (tool.Name, "la raíz de parameters debe tener type=object");

            var schemaError = ValidateSchemaNode(tool.ParametersSchema, "$", requirePropertiesForRequired: true);
            if (schemaError is not null)
                return (tool.Name, schemaError);
        }

        return null;
    }

    private static string? ValidateSchemaNode(
        JsonElement schema,
        string path,
        bool requirePropertiesForRequired = false)
    {
        if (schema.ValueKind != JsonValueKind.Object)
            return $"{path} debe ser un objeto de esquema JSON";

        JsonElement properties = default;
        var hasProperties = schema.TryGetProperty("properties", out properties);
        if (hasProperties && properties.ValueKind != JsonValueKind.Object)
            return $"{path}.properties debe ser un objeto";

        if (hasProperties)
        {
            foreach (var property in properties.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                    return $"{path}.properties.{property.Name} debe ser un objeto de esquema";
                var nestedError = ValidateSchemaNode(
                    property.Value,
                    $"{path}.properties.{property.Name}");
                if (nestedError is not null)
                    return nestedError;
            }
        }

        if (schema.TryGetProperty("required", out var required))
        {
            if (required.ValueKind != JsonValueKind.Array)
                return $"{path}.required debe ser un arreglo";
            if (requirePropertiesForRequired && !hasProperties)
                return $"{path}.required exige que properties esté definido";

            var requiredNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in required.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                    return $"{path}.required solo puede contener nombres no vacíos";
                var requiredName = item.GetString()!;
                if (!requiredNames.Add(requiredName))
                    return $"{path}.required contiene el nombre duplicado '{requiredName}'";
                if (!hasProperties || !properties.TryGetProperty(requiredName, out _))
                    return $"{path}.required contiene la propiedad inexistente '{requiredName}'";
            }
        }

        if (schema.TryGetProperty("items", out var items)
            && items.ValueKind is not (JsonValueKind.Object or JsonValueKind.True or JsonValueKind.False))
            return $"{path}.items debe ser un objeto de esquema o un booleano";
        if (items.ValueKind == JsonValueKind.Object)
        {
            var itemsError = ValidateSchemaNode(items, $"{path}.items");
            if (itemsError is not null)
                return itemsError;
        }

        if (schema.TryGetProperty("additionalProperties", out var additional)
            && additional.ValueKind is not (JsonValueKind.Object or JsonValueKind.True or JsonValueKind.False))
            return $"{path}.additionalProperties debe ser un objeto de esquema o un booleano";

        return null;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex ToolNameRegex();
}
