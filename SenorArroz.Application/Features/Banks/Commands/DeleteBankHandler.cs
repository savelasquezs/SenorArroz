// SenorArroz.Application/Features/Banks/Commands/DeleteBankHandler.cs
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Banks.Commands;

public class DeleteBankHandler : IRequestHandler<DeleteBankCommand, bool>
{
    private readonly IBankRepository _bankRepository;
    private readonly ICurrentUser _currentUser;

    public DeleteBankHandler(IBankRepository bankRepository, ICurrentUser currentUser)
    {
        _bankRepository = bankRepository;
        _currentUser = currentUser;
    }

    public async Task<bool> Handle(DeleteBankCommand request, CancellationToken cancellationToken)
    {
        // Validate bank exists
        var existingBank = await _bankRepository.GetByIdAsync(request.Id);
        if (existingBank == null)
            return false;

        // Check if user has access to this bank's branch
        if (_currentUser.Role != "superadmin" && existingBank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para eliminar este banco");

        return await _bankRepository.DeleteAsync(request.Id);
    }
}
