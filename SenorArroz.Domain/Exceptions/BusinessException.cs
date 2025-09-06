// SenorArroz.Domain/Exceptions/BusinessException.cs
namespace SenorArroz.Domain.Exceptions;

/// <summary>
/// Excepción para errores de lógica de negocio
/// </summary>
public class BusinessException : Exception
{
    public BusinessException(string message) : base(message)
    {
    }

    public BusinessException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Excepción cuando no se encuentra un recurso
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }

    public NotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Excepción para validaciones
/// </summary>
public class ValidationException : Exception
{
    public IDictionary<string, string[]> Errors { get; }

    public ValidationException() : base("Se produjeron uno o más errores de validación")
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationException(IDictionary<string, string[]> errors) : this()
    {
        Errors = errors;
    }
}