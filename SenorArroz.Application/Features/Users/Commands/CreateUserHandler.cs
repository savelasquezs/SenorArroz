using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Interfaces.Services;

namespace SenorArroz.Application.Features.Users.Commands;

public class CreateUserHandler(IUserRepository userRepository, IPasswordService passwordService, IMapper mapper,
    ICurrentUser currentUser, IBranchContext branchContext, ICurrentTenant currentTenant, IApplicationDbContext db)
    : IRequestHandler<CreateUserCommand, UserDto>
{
    public async Task<UserDto> Handle(CreateUserCommand request, CancellationToken ct)
    {
        var input = request.UserData;
        input.BranchId = branchContext.RequireBranch(input.BranchId);
        if (!currentTenant.HasTenant) throw new BusinessException("No se pudo determinar el tenant autenticado.");
        var branch = await db.Branches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.BranchId && x.TenantId == currentTenant.TenantId, ct)
            ?? throw new BusinessException("La sucursal no pertenece al tenant autenticado.");
        if (await userRepository.EmailExistsAsync(input.Email, cancellationToken: ct))
            throw new BusinessException($"Ya existe un usuario con el email '{input.Email}'");
        if (Roles.IsAdmin(currentUser.Role))
        {
            if (currentUser.BranchId <= 0 || currentUser.BranchId != input.BranchId)
                throw new BusinessException("Un administrador solo puede crear usuarios de su sucursal.");
            if (input.Role is UserRole.Admin or UserRole.Superadmin)
                throw new BusinessException("Un administrador no puede crear usuarios con rol Admin o Superadmin.");
        }
        else if (Roles.IsSuperadmin(currentUser.Role))
        {
            if (input.Role == UserRole.Superadmin && await userRepository.RoleExistsAsync(UserRole.Superadmin, ct))
                throw new BusinessException("Solo puede existir un Superadmin en la aplicación.");
            if (input.Role == UserRole.Admin && await userRepository.AdminExistsInBranchAsync(input.BranchId, ct))
                throw new BusinessException("Ya existe un Admin en esta sucursal.");
        }
        else throw new UnauthorizedAccessException();
        if (!Enum.IsDefined(input.AccountType)) throw new BusinessException("Tipo de cuenta inválido.");
        if (input.AccountType == EmployeeAccountType.Terminal)
        {
            if (input.Role != UserRole.Kitchen || input.PayrollExpenseId.HasValue || input.DeliveryAppEnabled)
                throw new BusinessException("La terminal de cocina no puede tener nómina ni acceso a domicilios.");
            var actor = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == currentUser.Id && x.Active, ct)
                ?? throw new UnauthorizedAccessException();
            try { EmployeePasswordConfirmation.Verify(actor, input.ConfirmationPassword, passwordService); }
            finally { input.ConfirmationPassword = null; }
        }
        await UserPayrollExpenseRules.ValidatePayrollExpenseAssignmentAsync(input.PayrollExpenseId, null, db, ct);
        if (input.DeliveryAppEnabled && input.Role is not (UserRole.Admin or UserRole.Cashier))
            throw new BusinessException("El acceso adicional a domicilios solo aplica a administradores y cajeros.");
        var user = mapper.Map<User>(input);
        user.TenantId = branch.TenantId;
        user.AccountType = input.AccountType;
        user.DeliveryAppEnabled = input.DeliveryAppEnabled;
        user.WebAccessEnabled = user.Role == UserRole.Kitchen ? input.WebAccessEnabled ?? true : user.Role != UserRole.Deliveryman;
        user.PasswordHash = passwordService.HashPassword(input.Password);
        var created = await userRepository.AddAsync(user, ct);
        return mapper.Map<UserDto>(await userRepository.GetByIdAsync(created.Id, ct) ?? created);
    }
}
