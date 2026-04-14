namespace SenorArroz.Application.Common.Interfaces;

/// <summary>
/// Abstracción del reloj UTC para lógica de negocio y pruebas deterministas.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
