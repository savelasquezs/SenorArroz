using FluentValidation;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Customers.Commands;

namespace SenorArroz.Application.Features.Customers.Validators;

public class UpdateCustomerValidator : AbstractValidator<UpdateCustomerCommand>
{
    public UpdateCustomerValidator()
    {
        RuleFor(x => x.Id)
            .GreaterThan(0).WithMessage("ID del cliente es requerido");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("El nombre es requerido")
            .MaximumLength(150).WithMessage("El nombre no puede exceder 150 caracteres")
            .Matches(@"^[a-zA-ZáéíóúÁÉÍÓÚñÑüÜ\s]+$").WithMessage("El nombre solo puede contener letras y espacios");

        RuleFor(x => x.Phone1)
            .Matches(@"^\d{10}$").WithMessage("El teléfono debe tener exactamente 10 dígitos")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone1));

        RuleFor(x => x.Phone2)
            .Matches(@"^\d{10}$").WithMessage("El teléfono secundario debe tener exactamente 10 dígitos")
            .When(x => !string.IsNullOrEmpty(x.Phone2));

        RuleFor(x => x.Phone2)
            .Empty().WithMessage("El teléfono secundario requiere un teléfono principal")
            .When(x => string.IsNullOrWhiteSpace(x.Phone1));

        RuleFor(x => x.WhatsAppUsername)
            .Must(WhatsAppIdentityNormalizer.IsValidUsername)
            .WithMessage("El usuario de WhatsApp no tiene un formato válido")
            .When(x => !string.IsNullOrWhiteSpace(x.WhatsAppUsername));

        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Phone1) || !string.IsNullOrWhiteSpace(x.WhatsAppUsername))
            .WithMessage("Debe indicar un teléfono principal o un usuario de WhatsApp");
    }
}
