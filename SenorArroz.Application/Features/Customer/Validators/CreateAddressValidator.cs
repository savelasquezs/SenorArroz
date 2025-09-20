using FluentValidation;
using SenorArroz.Application.Features.Customers.Commands;

namespace SenorArroz.Application.Features.Customers.Validators;

public class CreateAddressValidator : AbstractValidator<CreateAddressCommand>
{
    public CreateAddressValidator()
    {
        RuleFor(x => x.CustomerId)
            .GreaterThan(0).WithMessage("ID del cliente es requerido");

        RuleFor(x => x.NeighborhoodId)
            .GreaterThan(0).WithMessage("El barrio es requerido");

        RuleFor(x => x.Address)
            .NotEmpty().WithMessage("La dirección es requerida")
            .MaximumLength(200).WithMessage("La dirección no puede exceder 200 caracteres");

        RuleFor(x => x.AdditionalInfo)
            .MaximumLength(150).WithMessage("La información adicional no puede exceder 150 caracteres")
            .When(x => !string.IsNullOrEmpty(x.AdditionalInfo));

        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90, 90).WithMessage("La latitud debe estar entre -90 y 90")
            .When(x => x.Latitude.HasValue);

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180, 180).WithMessage("La longitud debe estar entre -180 y 180")
            .When(x => x.Longitude.HasValue);
    }
}