using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BusinessDocuments.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Constants;

namespace SenorArroz.Application.Features.BusinessDocuments.Commands;

public sealed record CreateBusinessDocumentCommand(
    string Name,
    byte[] Content,
    string OriginalFileName,
    string ContentType) : IRequest<BusinessDocumentDto>;

public sealed class CreateBusinessDocumentHandler
    : IRequestHandler<CreateBusinessDocumentCommand, BusinessDocumentDto>
{
    private readonly IBusinessDocumentRepository _repository;
    private readonly IBusinessDocumentStorage _storage;
    private readonly ICurrentUser _currentUser;
    private readonly BusinessDocumentDtoFactory _dtoFactory;
    private readonly ILogger<CreateBusinessDocumentHandler> _logger;

    public CreateBusinessDocumentHandler(
        IBusinessDocumentRepository repository,
        IBusinessDocumentStorage storage,
        ICurrentUser currentUser,
        IOptions<ApiPublicOptions> apiPublicOptions,
        ILogger<CreateBusinessDocumentHandler> logger)
    {
        _repository = repository;
        _storage = storage;
        _currentUser = currentUser;
        _dtoFactory = new BusinessDocumentDtoFactory(apiPublicOptions);
        _logger = logger;
    }

    public async Task<BusinessDocumentDto> Handle(
        CreateBusinessDocumentCommand request,
        CancellationToken cancellationToken)
    {
        EnsureSuperadmin();
        var name = BusinessDocumentFilePolicy.ValidateName(request.Name);
        var originalFileName = BusinessDocumentFilePolicy.ValidateFile(
            request.Content,
            request.OriginalFileName,
            request.ContentType);

        var publicId = Guid.NewGuid();
        var stored = await _storage.UploadAsync(publicId, request.Content, cancellationToken);
        var now = DateTime.UtcNow;
        var document = new BusinessDocument
        {
            PublicId = publicId,
            Name = name,
            DownloadUrl = stored.DownloadUrl,
            StorageObjectName = stored.ObjectName,
            OriginalFileName = originalFileName,
            ContentType = "application/pdf",
            FileSizeBytes = request.Content.LongLength,
            CreatedAt = now,
            UpdatedAt = now,
        };

        try
        {
            await _repository.CreateAsync(document, cancellationToken);
        }
        catch
        {
            await TryDeleteObjectAsync(stored.ObjectName, cancellationToken);
            throw;
        }

        return _dtoFactory.Create(document);
    }

    private void EnsureSuperadmin()
    {
        if (!Roles.IsSuperadmin(_currentUser.Role))
            throw new UnauthorizedAccessException("Solo Superadmin puede crear documentos.");
    }

    private async Task TryDeleteObjectAsync(string objectName, CancellationToken cancellationToken)
    {
        try
        {
            await _storage.DeleteObjectAsync(objectName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "No se pudo compensar la carga del documento en Firebase. ObjectName={ObjectName}",
                objectName);
        }
    }
}
