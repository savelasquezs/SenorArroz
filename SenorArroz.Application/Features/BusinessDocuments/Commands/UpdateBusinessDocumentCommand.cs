using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BusinessDocuments.DTOs;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Constants;

namespace SenorArroz.Application.Features.BusinessDocuments.Commands;

public sealed record UpdateBusinessDocumentCommand(
    int Id,
    string Name,
    byte[]? Content,
    string? OriginalFileName,
    string? ContentType) : IRequest<BusinessDocumentDto>;

public sealed class UpdateBusinessDocumentHandler
    : IRequestHandler<UpdateBusinessDocumentCommand, BusinessDocumentDto>
{
    private readonly IBusinessDocumentRepository _repository;
    private readonly IBusinessDocumentStorage _storage;
    private readonly ICurrentUser _currentUser;
    private readonly BusinessDocumentDtoFactory _dtoFactory;
    private readonly ILogger<UpdateBusinessDocumentHandler> _logger;

    public UpdateBusinessDocumentHandler(
        IBusinessDocumentRepository repository,
        IBusinessDocumentStorage storage,
        ICurrentUser currentUser,
        IOptions<ApiPublicOptions> apiPublicOptions,
        ILogger<UpdateBusinessDocumentHandler> logger)
    {
        _repository = repository;
        _storage = storage;
        _currentUser = currentUser;
        _dtoFactory = new BusinessDocumentDtoFactory(apiPublicOptions);
        _logger = logger;
    }

    public async Task<BusinessDocumentDto> Handle(
        UpdateBusinessDocumentCommand request,
        CancellationToken cancellationToken)
    {
        EnsureSuperadmin();
        var name = BusinessDocumentFilePolicy.ValidateName(request.Name);
        var document = await _repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException($"Documento con ID {request.Id} no encontrado.");

        StoredBusinessDocumentFile? newStoredFile = null;
        var previousObjectName = document.StorageObjectName;

        if (request.Content is not null)
        {
            var originalFileName = BusinessDocumentFilePolicy.ValidateFile(
                request.Content,
                request.OriginalFileName,
                request.ContentType);
            newStoredFile = await _storage.UploadAsync(
                document.PublicId,
                request.Content,
                cancellationToken);
            document.DownloadUrl = newStoredFile.DownloadUrl;
            document.StorageObjectName = newStoredFile.ObjectName;
            document.OriginalFileName = originalFileName;
            document.ContentType = "application/pdf";
            document.FileSizeBytes = request.Content.LongLength;
        }

        document.Name = name;
        document.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _repository.UpdateAsync(document, cancellationToken);
        }
        catch
        {
            if (newStoredFile is not null)
                await TryDeleteObjectAsync(newStoredFile.ObjectName, cancellationToken, "compensar");
            throw;
        }

        if (newStoredFile is not null &&
            !string.Equals(previousObjectName, newStoredFile.ObjectName, StringComparison.Ordinal))
        {
            await TryDeleteObjectAsync(previousObjectName, cancellationToken, "limpiar versión anterior");
        }

        return _dtoFactory.Create(document);
    }

    private void EnsureSuperadmin()
    {
        if (!Roles.IsSuperadmin(_currentUser.Role))
            throw new UnauthorizedAccessException("Solo Superadmin puede actualizar documentos.");
    }

    private async Task TryDeleteObjectAsync(
        string objectName,
        CancellationToken cancellationToken,
        string operation)
    {
        try
        {
            await _storage.DeleteObjectAsync(objectName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "No se pudo {Operation} en Firebase. ObjectName={ObjectName}",
                operation,
                objectName);
        }
    }
}
