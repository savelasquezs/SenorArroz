using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;

namespace SenorArroz.Infrastructure.WhatsApp;

public sealed class WhatsAppFlowCrypto(IOptions<WhatsAppFlowOptions> options) : IWhatsAppFlowCrypto
{
    private readonly WhatsAppFlowOptions _options = options.Value;

    public WhatsAppDecryptedFlowRequest Decrypt(WhatsAppEncryptedFlowRequest request)
    {
        if (string.IsNullOrWhiteSpace(_options.PrivateKey)) throw new InvalidOperationException("WhatsApp Flow no tiene clave privada configurada.");
        using var rsa = RSA.Create();
        if (string.IsNullOrEmpty(_options.PrivateKeyPassphrase)) rsa.ImportFromPem(_options.PrivateKey);
        else rsa.ImportFromEncryptedPem(_options.PrivateKey, _options.PrivateKeyPassphrase);
        var aesKey = rsa.Decrypt(Convert.FromBase64String(request.EncryptedAesKey), RSAEncryptionPadding.OaepSHA256);
        var iv = Convert.FromBase64String(request.InitialVector);
        var encrypted = Convert.FromBase64String(request.EncryptedFlowData);
        if (aesKey.Length is not (16 or 24 or 32) || iv.Length != 16 || encrypted.Length <= 16)
            throw new CryptographicException("Payload cifrado inválido.");
        var plaintext = TransformAesGcm(false, encrypted, aesKey, iv);
        return new(Encoding.UTF8.GetString(plaintext), aesKey, iv);
    }

    public string Encrypt(string responseJson, byte[] aesKey, byte[] initialVector)
    {
        var iv = initialVector.Select(value => (byte)~value).ToArray();
        return Convert.ToBase64String(TransformAesGcm(true, Encoding.UTF8.GetBytes(responseJson), aesKey, iv));
    }

    private static byte[] TransformAesGcm(bool encrypt, byte[] input, byte[] key, byte[] iv)
    {
        try
        {
            var cipher = new GcmBlockCipher(new AesEngine());
            cipher.Init(encrypt, new AeadParameters(new KeyParameter(key), 128, iv));
            var output = new byte[cipher.GetOutputSize(input.Length)];
            var length = cipher.ProcessBytes(input, 0, input.Length, output, 0);
            length += cipher.DoFinal(output, length);
            return output.AsSpan(0, length).ToArray();
        }
        catch (InvalidCipherTextException ex)
        {
            throw new CryptographicException("Payload cifrado inválido.", ex);
        }
    }
}
