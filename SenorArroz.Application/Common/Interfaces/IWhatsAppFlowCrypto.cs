namespace SenorArroz.Application.Common.Interfaces;

public sealed record WhatsAppEncryptedFlowRequest(string EncryptedAesKey,string EncryptedFlowData,string InitialVector);
public sealed record WhatsAppDecryptedFlowRequest(string Json,byte[] AesKey,byte[] InitialVector);

public interface IWhatsAppFlowCrypto
{
    WhatsAppDecryptedFlowRequest Decrypt(WhatsAppEncryptedFlowRequest request);
    string Encrypt(string responseJson,byte[] aesKey,byte[] initialVector);
}
