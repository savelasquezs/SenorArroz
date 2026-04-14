using FluentValidation;
using MediatR;
using SenorArroz.Application.Common.Behaviors;
using DomainValidationException = SenorArroz.Domain.Exceptions.ValidationException;

namespace SenorArroz.Tests;

public class ValidationBehaviorTests
{
    private sealed record DummyRequest(string Name) : IRequest<Unit>;

    private sealed class DummyValidator : AbstractValidator<DummyRequest>
    {
        public DummyValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("El nombre es requerido");
        }
    }

    [Fact]
    public async Task When_invalid_throws_domain_ValidationException_with_errors()
    {
        IValidator<DummyRequest>[] validators = [new DummyValidator()];
        var behavior = new ValidationBehavior<DummyRequest, Unit>(validators);
        var request = new DummyRequest("");

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
            behavior.Handle(request, _ => Task.FromResult(Unit.Value), CancellationToken.None));

        Assert.NotEmpty(ex.Errors);
        var messages = ex.Errors.Values.SelectMany(a => a).ToList();
        Assert.Contains(messages, m => m.Contains("nombre", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task When_valid_invokes_next()
    {
        IValidator<DummyRequest>[] validators = [new DummyValidator()];
        var behavior = new ValidationBehavior<DummyRequest, Unit>(validators);
        var called = false;

        await behavior.Handle(
            new DummyRequest("Ana"),
            _ =>
            {
                called = true;
                return Task.FromResult(Unit.Value);
            },
            CancellationToken.None);

        Assert.True(called);
    }

    [Fact]
    public async Task When_no_validators_invokes_next_without_validation()
    {
        var behavior = new ValidationBehavior<DummyRequest, Unit>(Array.Empty<IValidator<DummyRequest>>());
        var called = false;

        await behavior.Handle(
            new DummyRequest(""),
            _ =>
            {
                called = true;
                return Task.FromResult(Unit.Value);
            },
            CancellationToken.None);

        Assert.True(called);
    }
}
