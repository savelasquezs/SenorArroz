using System.Text.Json;
using System.Text.Json.Serialization;
using SenorArroz.Domain.Enums;

namespace SenorArroz.API.Extensions;

/// <summary>
/// Mantiene compatibilidad con las versiones móviles que enviaban
/// ACTIVE_DELIVERY y con el contrato canónico snake_case del API.
/// </summary>
public sealed class DeliveryTrackingModeJsonConverter
    : JsonConverter<DeliveryTrackingMode?>
{
    public override DeliveryTrackingMode? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.Number
            && reader.TryGetInt32(out var numericValue)
            && Enum.IsDefined(typeof(DeliveryTrackingMode), numericValue))
        {
            return (DeliveryTrackingMode)numericValue;
        }

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("El modo de seguimiento no es válido.");

        var raw = reader.GetString()?.Trim();
        var normalized = raw?
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        if (!string.IsNullOrWhiteSpace(normalized)
            && Enum.TryParse<DeliveryTrackingMode>(
                normalized,
                ignoreCase: true,
                out var result)
            && Enum.IsDefined(result))
        {
            return result;
        }

        throw new JsonException("El modo de seguimiento no es válido.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        DeliveryTrackingMode? value,
        JsonSerializerOptions options)
    {
        if (!value.HasValue)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value switch
        {
            DeliveryTrackingMode.Light => "light",
            DeliveryTrackingMode.ActiveDelivery => "active_delivery",
            DeliveryTrackingMode.Offline => "offline",
            DeliveryTrackingMode.Stopped => "stopped",
            _ => throw new JsonException("El modo de seguimiento no es válido."),
        });
    }
}
