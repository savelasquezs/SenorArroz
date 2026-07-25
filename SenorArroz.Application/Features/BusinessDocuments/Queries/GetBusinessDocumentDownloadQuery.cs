using MediatR;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.BusinessDocuments.Queries;

public sealed record GetBusinessDocumentDownloadQuery(Guid PublicId) : IRequest<string>;

public sealed class GetBusinessDocumentDownloadHandler
    : IRequestHandler<GetBusinessDocumentDownloadQuery, string>
{
    private readonly IBusinessDocumentRepository _repository;

    public GetBusinessDocumentDownloadHandler(IBusinessDocumentRepository repository)
    {
        _repository = repository;
    }

    public async Task<string> Handle(
        GetBusinessDocumentDownloadQuery request,
        CancellationToken cancellationToken)
    {
        var document = await _repository.GetByPublicIdAsync(request.PublicId, cancellationToken)
            ?? throw new NotFoundException("Documento no encontrado.");
        return document.DownloadUrl;
    }
}
