using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Constants;

namespace SenorArroz.Application.Features.BusinessDocuments.Commands;

public sealed record DeleteBusinessDocumentCommand(int Id) : IRequest;

public sealed class DeleteBusinessDocumentHandler
    : IRequestHandler<DeleteBusinessDocumentCommand>
{
    private readonly IBusinessDocumentRepository _repository;
    private readonly IBusinessDocumentStorage _storage;
    private readonly ICurrentUser _currentUser;

    public DeleteBusinessDocumentHandler(
        IBusinessDocumentRepository repository,
        IBusinessDocumentStorage storage,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _storage = storage;
        _currentUser = currentUser;
    }

    public async Task Handle(
        DeleteBusinessDocumentCommand request,
        CancellationToken cancellationToken)
    {
        if (!Roles.IsSuperadmin(_currentUser.Role))
            throw new UnauthorizedAccessException("Solo Superadmin puede eliminar documentos.");

        var document = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (document is null)
            return;

        await _storage.DeleteDocumentAsync(document.PublicId, cancellationToken);
        await _repository.DeleteAsync(document, cancellationToken);
    }
}
