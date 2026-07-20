using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using SenorArroz.Application.Common.Interfaces;

namespace SenorArroz.Infrastructure.Integrations;

public sealed class IntegrationSecretProtector(IConfiguration configuration) : IIntegrationSecretProtector
{
    public string Protect(string plainText)
    {
        var key = GetKey();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[Encoding.UTF8.GetByteCount(plainText)];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, Encoding.UTF8.GetBytes(plainText), cipher, tag);
        return $"v1.{Convert.ToBase64String(nonce)}.{Convert.ToBase64String(tag)}.{Convert.ToBase64String(cipher)}";
    }

    public string Unprotect(string protectedText)
    {
        var parts = protectedText.Split('.');
        if (parts.Length != 4 || parts[0] != "v1")
            throw new CryptographicException("Formato de credencial cifrada inválido.");
        var nonce = Convert.FromBase64String(parts[1]);
        var tag = Convert.FromBase64String(parts[2]);
        var cipher = Convert.FromBase64String(parts[3]);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(GetKey(), 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    private byte[] GetKey()
    {
        var configured = configuration["Integrations:EncryptionKey"];
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("Configure Integrations__EncryptionKey antes de guardar credenciales de integraciones.");
        return SHA256.HashData(Encoding.UTF8.GetBytes(configured));
    }
}
