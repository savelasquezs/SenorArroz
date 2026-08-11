using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.Users.Commands;

public sealed record SetDeliverymanWebAccessCommand(int UserId, bool Enabled) : IRequest<UserDto>;

public sealed class SetDeliverymanWebAccessHandler(
    IApplicationDbContext db,
    IMapper mapper) : IRequestHandler<SetDeliverymanWebAccessCommand, UserDto>
{
    public async Task<UserDto> Handle(
        SetDeliverymanWebAccessCommand request,
        CancellationToken cancellationToken)
    {
        var user = await db.Users
            .Include(candidate => candidate.Branch)
            .Include(candidate => candidate.PayrollExpense)
            .FirstOrDefaultAsync(candidate => candidate.Id == request.UserId, cancellationToken)
            ?? throw new NotFoundException($"Usuario con ID {request.UserId} no encontrado");

        if (user.Role != UserRole.Deliveryman)
            throw new BusinessException("El permiso de acceso web solo aplica a domiciliarios.");

        user.WebAccessEnabled = request.Enabled;
        await db.SaveChangesAsync(cancellationToken);

        return mapper.Map<UserDto>(user);
    }
}
