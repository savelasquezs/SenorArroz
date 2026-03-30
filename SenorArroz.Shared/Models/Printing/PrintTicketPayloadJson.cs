using System.Text.Json;
using System.Text.Json.Serialization;

namespace SenorArroz.Shared.Models.Printing;

public static class PrintTicketPayloadJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string SerializeBatch(PrintTicketPayloadBatchV1 batch) =>
        JsonSerializer.Serialize(batch, Options);
}
