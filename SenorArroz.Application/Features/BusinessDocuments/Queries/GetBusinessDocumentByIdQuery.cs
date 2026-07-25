using MediatR;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Features.BusinessDocuments.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.BusinessDocuments.Queries;

public sealed record GetBusinessDocumentByIdQuery(int Id) : IRequest<BusinessDocumentDto>;

public sealed class GetBusinessDocumentByIdHandler
    : IRequestHandler<GetBusinessDocumentByIdQuery, BusinessDocumentDto>
{
    private readonly IBusinessDocumentRepository _repository;
    private readonly BusinessDocumentDtoFactory _dtoFactory;

    public GetBusinessDocumentByIdHandler(
        IBusinessDocumentRepository repository,
        IOptions<ApiPublicOptions> apiPublicOptions)
    {
        _repository = repository;
        _dtoFactory = new BusinessDocumentDtoFactory(apiPublicOptions);
    }

    public async Task<BusinessDocumentDto> Handle(
        GetBusinessDocumentByIdQuery request,
        CancellationToken cancellationToken)
    {
        var document = await _repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException($"Documento con ID {request.Id} no encontrado.");
        return _dtoFactory.Create(document);
    }
}
