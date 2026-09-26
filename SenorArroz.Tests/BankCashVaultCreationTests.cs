using AutoMapper;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Banks.Commands;
using SenorArroz.Application.Features.Banks.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Tests;

public sealed class BankCashVaultCreationTests
{
    private sealed class AdminUser : ICurrentUser
    {
        public int Id => 10;
        public string Role => "Admin";
        public int BranchId => 2;
        public bool IsAuthenticated => true;
    }

    [Fact]
    public async Task CreateCashVault_CanonicalizesAndPersistsCashVaultType()
    {
        var banks = new Mock<IBankRepository>();
        banks
            .Setup(x => x.TypeExistsInBranchAsync(BankType.CashVault, 2, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        banks
            .Setup(x => x.NameExistsInBranchAsync("Caja Mayor Efectivo", 2, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        banks
            .Setup(x => x.CreateAsync(It.IsAny<Bank>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Bank bank, CancellationToken _) =>
            {
                bank.Id = 99;
                return bank;
            });

        var branches = new Mock<IBranchRepository>();
        branches.Setup(x => x.ExistsAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var mapper = new Mock<IMapper>();
        mapper
            .Setup(x => x.Map<BankDto>(It.IsAny<object>()))
            .Returns((object source) =>
            {
                var bank = (Bank)source;
                return new BankDto
                {
                    Id = bank.Id,
                    BranchId = bank.BranchId,
                    Name = bank.Name,
                    Active = bank.Active,
                    Type = bank.Type
                };
            });

        var handler = new CreateBankHandler(
            banks.Object,
            branches.Object,
            mapper.Object,
            new AdminUser(),
            new TestBranchContext(2));

        var result = await handler.Handle(
            new CreateBankCommand
            {
                BranchId = 2,
                Name = "cualquier nombre",
                ImageUrl = "https://example.com/logo.png",
                Active = false,
                Type = BankType.CashVault
            },
            CancellationToken.None);

        Assert.Equal(BankType.CashVault, result.Type);
        Assert.Equal("Caja Mayor Efectivo", result.Name);
        Assert.True(result.Active);
        banks.Verify(x => x.CreateAsync(
            It.Is<Bank>(b =>
                b.BranchId == 2 &&
                b.Type == BankType.CashVault &&
                b.Name == "Caja Mayor Efectivo" &&
                b.ImageUrl == null &&
                b.Active),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateCashVault_RejectsSecondVaultInSameBranch()
    {
        var banks = new Mock<IBankRepository>();
        banks
            .Setup(x => x.TypeExistsInBranchAsync(BankType.CashVault, 2, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var branches = new Mock<IBranchRepository>();
        branches.Setup(x => x.ExistsAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var handler = new CreateBankHandler(
            banks.Object,
            branches.Object,
            Mock.Of<IMapper>(),
            new AdminUser(),
            new TestBranchContext(2));

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            handler.Handle(
                new CreateBankCommand
                {
                    BranchId = 2,
                    Name = "Caja Mayor Efectivo",
                    Type = BankType.CashVault
                },
                CancellationToken.None));

        Assert.Contains("ya tiene una Caja Mayor Efectivo", ex.Message);
        banks.Verify(x => x.CreateAsync(It.IsAny<Bank>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
