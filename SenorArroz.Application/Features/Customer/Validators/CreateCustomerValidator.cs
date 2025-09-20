using FluentValidation;
using SenorArroz.Application.Features.Customers.Commands;

namespace SenorArroz.Application.Features.Customers.Validators;

public class CreateCustomerValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("El nombre es requerido")
            .MaximumLength(150).WithMessage("El nombre no puede exceder 150 caracteres")
            .Matches(@"^[a-zA-ZáéíóúÁÉÍÓÚñÑüÜ\s]+$").WithMessage("El nombre solo puede contener letras y espacios");

        RuleFor(x => x.Phone1)
            .NotEmpty().WithMessage("El teléfono principal es requerido")
            .Matches(@"^\d{10}$").WithMessage("El teléfono debe tener exactamente 10 dígitos");

        RuleFor(x => x.Phone2)
            .Matches(@"^\d{10}$").WithMessage("El teléfono secundario debe tener exactamente 10 dígitos")
            .When(x => !string.IsNullOrEmpty(x.Phone2));

        RuleFor(x => x.BranchId)
            .GreaterThan(0).WithMessage("La sucursal es requerida");

        // Validate initial address if provided
        RuleFor(x => x.InitialAddress.NeighborhoodId)
            .GreaterThan(0).WithMessage("El barrio es requerido")
            .When(x => x.InitialAddress != null);

        RuleFor(x => x.InitialAddress.Address)
            .NotEmpty().WithMessage("La dirección es requerida")
            .MaximumLength(200).WithMessage("La dirección no puede exceder 200 caracteres")
            .When(x => x.InitialAddress != null);

        RuleFor(x => x.InitialAddress.AdditionalInfo)
            .MaximumLength(150).WithMessage("La información adicional no puede exceder 150 caracteres")
            .When(x => x.InitialAddress != null && !string.IsNullOrEmpty(x.InitialAddress.AdditionalInfo));
    }
}