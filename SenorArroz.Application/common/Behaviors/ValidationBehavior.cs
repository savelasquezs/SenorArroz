using FluentValidation;
using MediatR;
using DomainValidationException = SenorArroz.Domain.Exceptions.ValidationException;

namespace SenorArroz.Application.Common.Behaviors;

/// <summary>
/// Ejecuta todos los <see cref="IValidator{T}"/> registrados para la petición MediatR
/// y lanza <see cref="SenorArroz.Domain.Exceptions.ValidationException"/> del dominio si hay fallos.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next(cancellationToken).ConfigureAwait(false);

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(
                _validators.Select(v => v.ValidateAsync(context, cancellationToken)))
            .ConfigureAwait(false);

        var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();
        if (failures.Count == 0)
            return await next(cancellationToken).ConfigureAwait(false);

        var errors = failures
            .GroupBy(f => string.IsNullOrEmpty(f.PropertyName) ? "_" : f.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).Distinct().ToArray(),
                StringComparer.Ordinal);

        throw new DomainValidationException(errors);
    }
}
