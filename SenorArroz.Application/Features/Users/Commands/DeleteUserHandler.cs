// SenorArroz.Application/Features/Users/Commands/DeleteUserHandler.cs
using MediatR;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Users.Commands
{
    public class DeleteUserHandler : IRequestHandler<DeleteUserCommand, Unit>
    {
        private readonly IUserRepository _userRepository;

        public DeleteUserHandler(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        public async Task<Unit> Handle(DeleteUserCommand request, CancellationToken cancellationToken)
        {
            // Verificar que el usuario existe y eliminarlo (soft delete)
            var deleted = await _userRepository.DeleteAsync(request.UserId, cancellationToken);

            if (!deleted)
            {
                throw new NotFoundException($"Usuario con ID {request.UserId} no encontrado");
            }

            return Unit.Value; // Unit.Value indica operación void exitosa
        }
    }

    // Comando para eliminar usuario
    public record DeleteUserCommand(int UserId) : IRequest<Unit>;
}