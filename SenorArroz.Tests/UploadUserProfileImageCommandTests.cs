using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Users.Commands;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public class UploadUserProfileImageCommandTests
{
    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeStorage(string returnUrl = "https://cdn.test/img.jpg") : IUserProfileImageStorage
    {
        public Task<string> SaveAndReplaceAsync(int userId, byte[] content, string fileExtension, CancellationToken cancellationToken = default)
            => Task.FromResult(returnUrl);
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly User _user;

        public FakeUserRepository(User user) => _user = user;

        public Task<User?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
            => Task.FromResult<User?>(_user);

        public Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default)
            => Task.FromResult(user);

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<User>> GetAllAsync(int? branchId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<User> AddAsync(User user, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> UpdateUserPasswordAsync(User user, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> EmailExistsAsync(string email, int? excludeUserId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> RoleExistsAsync(UserRole role, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> AdminExistsInBranchAsync(int branchId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> KitchenExistsInBranchAsync(int branchId, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private static readonly User TestUser = new()
    {
        Id = 1,
        Name = "Test User",
        Email = "test@test.com",
        Role = UserRole.Cashier,
        BranchId = 1,
        Active = true,
    };

    private static UploadUserProfileImageHandler BuildHandler(User? user = null)
        => new(new FakeStorage(), new FakeUserRepository(user ?? TestUser));

    private static byte[] OneMb() => new byte[1 * 1024 * 1024];
    private static byte[] FourMb() => new byte[4 * 1024 * 1024];

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Invalid_extension_throws_BusinessException()
    {
        var handler = BuildHandler();
        var command = new UploadUserProfileImageCommand(1, OneMb(), ".gif");

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            handler.Handle(command, CancellationToken.None));
        Assert.Contains("Formato", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(".bmp")]
    [InlineData(".svg")]
    [InlineData(".pdf")]
    public async Task Multiple_invalid_extensions_throw_BusinessException(string ext)
    {
        var handler = BuildHandler();
        var command = new UploadUserProfileImageCommand(1, OneMb(), ext);

        await Assert.ThrowsAsync<BusinessException>(() =>
            handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task File_exceeding_3MB_throws_BusinessException()
    {
        var handler = BuildHandler();
        var command = new UploadUserProfileImageCommand(1, FourMb(), ".jpg");

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            handler.Handle(command, CancellationToken.None));
        Assert.Contains("3 MB", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".png")]
    [InlineData(".webp")]
    public async Task Valid_extensions_do_not_throw(string ext)
    {
        var handler = BuildHandler();
        var command = new UploadUserProfileImageCommand(1, OneMb(), ext);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
    }

    [Fact]
    public async Task Exactly_3MB_does_not_throw()
    {
        var handler = BuildHandler();
        var exactly3Mb = new byte[3 * 1024 * 1024];
        var command = new UploadUserProfileImageCommand(1, exactly3Mb, ".png");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.NotNull(result);
    }
}
