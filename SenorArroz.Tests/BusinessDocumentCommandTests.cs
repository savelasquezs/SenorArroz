using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BusinessDocuments.Commands;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Tests;

public class BusinessDocumentCommandTests
{
    private sealed class CurrentUser(string role) : ICurrentUser
    {
        public int Id => 1;
        public string Role => role;
        public int BranchId => 0;
        public bool IsAuthenticated => true;
    }

    private sealed class RecordingStorage : IBusinessDocumentStorage
    {
        public List<string> DeletedObjects { get; } = [];
        public List<Guid> DeletedDocuments { get; } = [];
        public int UploadCount { get; private set; }

        public Task<StoredBusinessDocumentFile> UploadAsync(
            Guid publicId,
            byte[] content,
            CancellationToken cancellationToken = default)
        {
            UploadCount++;
            var objectName = $"business-documents/{publicId:D}/upload-{UploadCount}.pdf";
            return Task.FromResult(new StoredBusinessDocumentFile(
                $"https://firebase.test/{UploadCount}.pdf?token=test",
                objectName));
        }

        public Task DeleteObjectAsync(string objectName, CancellationToken cancellationToken = default)
        {
            DeletedObjects.Add(objectName);
            return Task.CompletedTask;
        }

        public Task DeleteDocumentAsync(Guid publicId, CancellationToken cancellationToken = default)
        {
            DeletedDocuments.Add(publicId);
            return Task.CompletedTask;
        }
    }

    private static byte[] ValidPdf() => "%PDF-1.7\n%%EOF"u8.ToArray();

    private static IOptions<ApiPublicOptions> PublicOptions() =>
        Options.Create(new ApiPublicOptions { BaseUrl = "https://api.test" });

    [Fact]
    public async Task Create_rejects_content_without_pdf_signature()
    {
        var repository = new Mock<IBusinessDocumentRepository>();
        var storage = new RecordingStorage();
        var handler = new CreateBusinessDocumentHandler(
            repository.Object,
            storage,
            new CurrentUser("superadmin"),
            PublicOptions(),
            NullLogger<CreateBusinessDocumentHandler>.Instance);

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            handler.Handle(
                new CreateBusinessDocumentCommand(
                    "Reglamento",
                    "not a pdf"u8.ToArray(),
                    "reglamento.pdf",
                    "application/pdf"),
                CancellationToken.None));

        Assert.Contains("contenido", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, storage.UploadCount);
    }

    [Fact]
    public async Task Create_rejects_non_superadmin_before_upload()
    {
        var repository = new Mock<IBusinessDocumentRepository>();
        var storage = new RecordingStorage();
        var handler = new CreateBusinessDocumentHandler(
            repository.Object,
            storage,
            new CurrentUser("admin"),
            PublicOptions(),
            NullLogger<CreateBusinessDocumentHandler>.Instance);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(
                new CreateBusinessDocumentCommand(
                    "Política",
                    ValidPdf(),
                    "politica.pdf",
                    "application/pdf"),
                CancellationToken.None));

        Assert.Equal(0, storage.UploadCount);
    }

    [Fact]
    public async Task Create_deletes_uploaded_object_when_database_write_fails()
    {
        var repository = new Mock<IBusinessDocumentRepository>();
        repository
            .Setup(x => x.CreateAsync(
                It.IsAny<BusinessDocument>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        var storage = new RecordingStorage();
        var handler = new CreateBusinessDocumentHandler(
            repository.Object,
            storage,
            new CurrentUser("superadmin"),
            PublicOptions(),
            NullLogger<CreateBusinessDocumentHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new CreateBusinessDocumentCommand(
                    "Política",
                    ValidPdf(),
                    "politica.pdf",
                    "application/pdf"),
                CancellationToken.None));

        Assert.Single(storage.DeletedObjects);
        Assert.EndsWith("upload-1.pdf", storage.DeletedObjects[0]);
    }

    [Fact]
    public async Task Update_replaces_file_without_changing_public_id()
    {
        var publicId = Guid.NewGuid();
        var document = new BusinessDocument
        {
            Id = 7,
            PublicId = publicId,
            Name = "Anterior",
            DownloadUrl = "https://firebase.test/old.pdf",
            StorageObjectName = $"business-documents/{publicId:D}/old.pdf",
            OriginalFileName = "old.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 10,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow.AddDays(-1),
        };
        var repository = new Mock<IBusinessDocumentRepository>();
        repository
            .Setup(x => x.GetByIdAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);
        repository
            .Setup(x => x.UpdateAsync(document, It.IsAny<CancellationToken>()))
            .ReturnsAsync(document);
        var storage = new RecordingStorage();
        var handler = new UpdateBusinessDocumentHandler(
            repository.Object,
            storage,
            new CurrentUser("superadmin"),
            PublicOptions(),
            NullLogger<UpdateBusinessDocumentHandler>.Instance);

        var result = await handler.Handle(
            new UpdateBusinessDocumentCommand(
                7,
                "Nueva política",
                ValidPdf(),
                "new.pdf",
                "application/pdf"),
            CancellationToken.None);

        Assert.Equal(publicId, result.PublicId);
        Assert.Equal("Nueva política", result.Name);
        Assert.Equal("new.pdf", result.OriginalFileName);
        Assert.Contains(publicId.ToString("D"), result.PublicDownloadUrl);
        Assert.Single(storage.DeletedObjects);
        Assert.EndsWith("old.pdf", storage.DeletedObjects[0]);
    }

    [Fact]
    public async Task Delete_is_idempotent_when_record_no_longer_exists()
    {
        var repository = new Mock<IBusinessDocumentRepository>();
        repository
            .Setup(x => x.GetByIdAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BusinessDocument?)null);
        var storage = new RecordingStorage();
        var handler = new DeleteBusinessDocumentHandler(
            repository.Object,
            storage,
            new CurrentUser("superadmin"));

        await handler.Handle(new DeleteBusinessDocumentCommand(7), CancellationToken.None);

        Assert.Empty(storage.DeletedDocuments);
        repository.Verify(
            x => x.DeleteAsync(
                It.IsAny<BusinessDocument>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
