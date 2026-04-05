using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace SenorArroz.API.Hosting;

/// <summary>
/// Deja listo <c>GOOGLE_APPLICATION_CREDENTIALS</c> para las librerías de Google (Storage, etc.).
/// Prioridad: ya definido en entorno → JSON en claro → JSON en Base64 → ruta en configuración.
/// </summary>
public static class GoogleCredentialBootstrap
{
    /// <summary>Contenido completo del JSON de cuenta de servicio (multilínea).</summary>
    public const string JsonEnvironmentVariable = "GOOGLE_APPLICATION_CREDENTIALS_JSON";

    /// <summary>Mismo JSON codificado en Base64 (una línea; cómodo para Railway).</summary>
    public const string JsonBase64EnvironmentVariable = "GOOGLE_APPLICATION_CREDENTIALS_JSON_BASE64";

    private const string ConfigPathKey = "FirebaseStorage:GoogleApplicationCredentialsPath";

    /// <summary>Si hay JSON/Base64 en entorno y no hay ruta ya fijada, escribe un archivo temporal y asigna la variable de ruta.</summary>
    public static void ApplyFromEnvironment()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS")))
            return;

        var json = Environment.GetEnvironmentVariable(JsonEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(json))
            json = TryDecodeJsonFromBase64();

        if (string.IsNullOrWhiteSpace(json))
            return;

        json = json.Trim();
        if (!json.StartsWith('{'))
            return;

        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return;
        }

        var stamp = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..16];
        var path = Path.Combine(Path.GetTempPath(), $"senorarroz-gcp-sa-{stamp}.json");
        if (!File.Exists(path))
            File.WriteAllText(path, json, Encoding.UTF8);

        Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", path);
    }

    private static string? TryDecodeJsonFromBase64()
    {
        var b64 = Environment.GetEnvironmentVariable(JsonBase64EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(b64))
            return null;

        b64 = b64.Trim().Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal);

        try
        {
            var bytes = Convert.FromBase64String(b64);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Si sigue sin haber ruta, usa <c>FirebaseStorage:GoogleApplicationCredentialsPath</c> (User Secrets, variable <c>FirebaseStorage__GoogleApplicationCredentialsPath</c>, etc.).
    /// </summary>
    public static void ApplyFromConfiguration(IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS")))
            return;

        var path = configuration[ConfigPathKey];
        if (string.IsNullOrWhiteSpace(path))
            return;

        var fullPath = Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));

        if (!File.Exists(fullPath))
            return;

        Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", fullPath);
    }
}
