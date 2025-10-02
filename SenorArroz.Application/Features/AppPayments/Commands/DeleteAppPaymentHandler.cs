// SenorArroz.Application/Features/AppPayments/Commands/DeleteAppPaymentHandler.cs
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.AppPayments.Commands;

public class DeleteAppPaymentHandler : IRequestHandler<DeleteAppPaymentCommand, bool>
{
    private readonly IAppPaymentRepository _appPaymentRepository;
    private readonly ICurrentUser _currentUser;

    public DeleteAppPaymentHandler(IAppPaymentRepository appPaymentRepository, ICurrentUser currentUser)
    {
        _appPaymentRepository = appPaymentRepository;
        _currentUser = currentUser;
    }

    public async Task<bool> Handle(DeleteAppPaymentCommand request, CancellationToken cancellationToken)
    {
        // Validate app payment exists
        var appPayment = await _appPaymentRepository.GetByIdAsync(request.Id);
        if (appPayment == null)
            return false;

        // Check if user has access to this app payment's branch
        if (_currentUser.Role != "superadmin" && appPayment.App.Bank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permisos para eliminar este pago");

        return await _appPaymentRepository.DeleteAsync(request.Id);
    }
}
