using System.Reflection;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BankPayments.Commands;
using SenorArroz.Application.Features.BankTransfers.Commands;
using SenorArroz.Application.Features.BankTransfers.DTOs;
using SenorArroz.Application.Features.BankTransfers.Queries;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public sealed class BankCashierPermissionsTests
{
    private sealed class CurrentUser(int branchId = 7) : ICurrentUser
    {
        public int Id => 15;
        public string Role => "Cashier";
        public int BranchId => branchId;
        public bool IsAuthenticated => true;
    }

    [Fact]
    public void Controllers_ExposeOnlyTheIntendedCashierPermissions()
    {
        var transfersRoles = typeof(BankTransfersController)
            .GetCustomAttribute<AuthorizeAttribute>()?.Roles;
        var verifyRoles = typeof(BankPaymentsController)
            .GetMethod(nameof(BankPaymentsController.VerifyBankPayment))!
            .GetCustomAttribute<AuthorizeAttribute>()?.Roles;
        var unverifyRoles = typeof(BankPaymentsController)
            .GetMethod(nameof(BankPaymentsController.UnverifyBankPayment))!
            .GetCustomAttribute<AuthorizeAttribute>()?.Roles;

        Assert.Contains("Cashier", transfersRoles ?? string.Empty);
        Assert.Contains("Cashier", verifyRoles ?? string.Empty);
        Assert.DoesNotContain("Cashier", unverifyRoles ?? string.Empty);
        Assert.Contains("Admin", unverifyRoles ?? string.Empty);
        Assert.Contains("Superadmin", unverifyRoles ?? string.Empty);
    }

    [Fact]
    public async Task Cashier_CanVerifyPaymentFromAssignedBranch()
    {
        var repository = new Mock<IBankPaymentRepository>();
        repository
            .Setup(x => x.GetByIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankPayment { Id = 10, Bank = new Bank { BranchId = 7 } });
        repository
            .Setup(x => x.VerifyPaymentAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new VerifyBankPaymentHandler(repository.Object, new CurrentUser(), new TestBranchContext(7));

        Assert.True(await handler.Handle(new VerifyBankPaymentCommand { Id = 10 }, CancellationToken.None));
        repository.Verify(x => x.VerifyPaymentAsync(10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cashier_CannotVerifyPaymentFromAnotherBranch()
    {
        var repository = new Mock<IBankPaymentRepository>();
        repository
            .Setup(x => x.GetByIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BankPayment { Id = 10, Bank = new Bank { BranchId = 8 } });

        var handler = new VerifyBankPaymentHandler(repository.Object, new CurrentUser(), new TestBranchContext(7));

        await Assert.ThrowsAsync<BranchScopeMismatchException>(() =>
            handler.Handle(new VerifyBankPaymentCommand { Id = 10 }, CancellationToken.None));
        repository.Verify(x => x.VerifyPaymentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cashier_CanCreateTransferInsideAssignedBranch()
    {
        var from = new Bank { Id = 1, BranchId = 7, Name = "Origen" };
        var to = new Bank { Id = 2, BranchId = 7, Name = "Destino" };
        var banks = new Mock<IBankRepository>();
        banks.Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(from);
        banks.Setup(x => x.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(to);

        var transfers = new Mock<IBankTransferRepository>();
        transfers
            .Setup(x => x.CreateAsync(It.IsAny<BankTransfer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BankTransfer transfer, CancellationToken _) => transfer);

        var mapper = new Mock<IMapper>();
        mapper
            .Setup(x => x.Map<BankTransferDto>(It.IsAny<object>()))
            .Returns((object source) =>
            {
                var transfer = (BankTransfer)source;
                return new BankTransferDto { Amount = transfer.Amount, CreatedById = transfer.CreatedById };
            });

        var handler = new CreateBankTransferHandler(
            transfers.Object,
            banks.Object,
            mapper.Object,
            new CurrentUser(),
            new TestBranchContext(7));

        var result = await handler.Handle(
            new CreateBankTransferCommand { FromBankId = 1, ToBankId = 2, Amount = 25000 },
            CancellationToken.None);

        Assert.Equal(25000, result.Amount);
        Assert.Equal(15, result.CreatedById);
        transfers.Verify(x => x.CreateAsync(
            It.Is<BankTransfer>(t => t.FromBankId == 1 && t.ToBankId == 2 && t.CreatedById == 15),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TransferListing_RejectsAnotherRequestedBranch()
    {
        var handler = new GetBankTransfersHandler(
            Mock.Of<IBankTransferRepository>(),
            Mock.Of<IMapper>(),
            new TestBranchContext(7));

        await Assert.ThrowsAsync<BranchScopeMismatchException>(() =>
            handler.Handle(new GetBankTransfersQuery { BranchId = 8 }, CancellationToken.None));
    }

    [Fact]
    public async Task TransferListing_IsScopedToAssignedBranch()
    {
        var repository = new Mock<IBankTransferRepository>();
        repository
            .Setup(x => x.GetPagedAsync(
                7, null, null, null, null, 1, 15, "createdAt", "desc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<BankTransfer> { Page = 1, PageSize = 15, TotalPages = 1 });

        var mapper = new Mock<IMapper>();
        mapper.Setup(x => x.Map<List<BankTransferDto>>(It.IsAny<object>())).Returns([]);

        var handler = new GetBankTransfersHandler(repository.Object, mapper.Object, new TestBranchContext(7));
        await handler.Handle(new GetBankTransfersQuery { Page = 1, PageSize = 15 }, CancellationToken.None);

        repository.Verify(x => x.GetPagedAsync(
            7, null, null, null, null, 1, 15, "createdAt", "desc", It.IsAny<CancellationToken>()), Times.Once);
    }
}
