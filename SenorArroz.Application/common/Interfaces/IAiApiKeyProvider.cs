namespace SenorArroz.Application.Common.Interfaces;

/// <summary>Resuelve credenciales de proveedores de IA exclusivamente desde el entorno del servidor.</summary>
public interface IAiApiKeyProvider
{
    string? GetApiKey(string provider);
    string GetEnvironmentVariableName(string provider);
}
