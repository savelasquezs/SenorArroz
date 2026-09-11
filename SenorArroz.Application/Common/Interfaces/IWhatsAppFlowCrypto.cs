using System.Text.Json.Serialization;

namespace SenorArroz.Application.Common.Interfaces;

public sealed record WhatsAppEncryptedFlowRequest(
    [property: JsonPropertyName("encrypted_aes_key")] string EncryptedAesKey,
    [property: JsonPropertyName("encrypted_flow_data")] string EncryptedFlowData,
    [property: JsonPropertyName("initial_vector")] string InitialVector);
public sealed record WhatsAppDecryptedFlowRequest(string Json,byte[] AesKey,byte[] InitialVector);

public interface IWhatsAppFlowCrypto
{
    WhatsAppDecryptedFlowRequest Decrypt(WhatsAppEncryptedFlowRequest request);
    string Encrypt(string responseJson,byte[] aesKey,byte[] initialVector);
}
