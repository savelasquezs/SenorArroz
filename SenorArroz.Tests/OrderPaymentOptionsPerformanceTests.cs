using AutoMapper;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Apps.DTOs;
using SenorArroz.Application.Features.Apps.Queries;
using SenorArroz.Application.Features.Banks.DTOs;
using SenorArroz.Application.Features.Banks.Queries;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

/// <summary>
/// M09-F06: order payment selection must not run settlement/balance aggregates
/// that the POS never displays; administrative requests must remain unchanged.
/// Uses repository method-call counts, not fabricated DB latency measurements.
/// </summary>
public sealed class OrderPaymentOptionsPerformanceTests
{
    [Fact]
    public async Task Bank_options_skip_per_bank_statistics_but_admin_list_retains_them()
    {
        var repo = new Mock<IBankRepository>();
        repo.Setup(x => x.GetPagedAsync(
                It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<bool?>(),
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<Bank>
            {
                Items = [
                    new Bank { Id = 11, BranchId = 1, Name = "Banco prueba", Active = true },
                    new Bank { Id = 12, BranchId = 1, Name = "Banco alterno", Active = true },
                ],
                TotalCount = 2, Page = 1, PageSize = 100, TotalPages = 1,
            });
        repo.Setup(x => x.GetTotalAppsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(2);
        repo.Setup(x => x.GetActiveAppsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        repo.Setup(x => x.GetCurrentBalanceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(700m);
        var mapper = new Mock<IMapper>();
        mapper.Setup(x => x.Map<BankDto>(It.IsAny<Bank>()))
            .Returns((Bank bank) => new BankDto
            {
                Id = bank.Id, Name = bank.Name, BranchId = bank.BranchId, Active = bank.Active,
            });
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.Role).Returns("Cashier");
        var context = new Mock<IBranchContext>();
        context.Setup(x => x.ResolveOptional(It.IsAny<int?>())).Returns(1);
        var handler = new GetBanksHandler(repo.Object, mapper.Object, user.Object, context.Object);

        var picker = await handler.Handle(new GetBanksQuery
            { BranchId = 1, ForOrderCreation = true, PageSize = 100 }, CancellationToken.None);
        Assert.Equal(2, picker.TotalCount);
        Assert.Equal(new[] { 11, 12 }, picker.Items.Select(b => b.Id));
        Assert.All(picker.Items, b =>
        {
            Assert.Equal(0, b.TotalApps);
            Assert.Equal(0, b.ActiveApps);
            Assert.Equal(0m, b.CurrentBalance);
        });
        repo.Verify(x => x.GetTotalAppsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(x => x.GetActiveAppsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(x => x.GetCurrentBalanceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

        var admin = await handler.Handle(new GetBanksQuery { BranchId = 1 }, CancellationToken.None);
        Assert.Equal(2, admin.Items.Count());
        Assert.All(admin.Items, b =>
        {
            Assert.Equal(2, b.TotalApps);
            Assert.Equal(1, b.ActiveApps);
            Assert.Equal(700m, b.CurrentBalance);
        });
        repo.Verify(x => x.GetTotalAppsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        repo.Verify(x => x.GetActiveAppsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        repo.Verify(x => x.GetCurrentBalanceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task App_options_skip_per_app_statistics_and_keep_branch_filter()
    {
        var repo = new Mock<IAppRepository>();
        repo.Setup(x => x.GetPagedAsync(
                It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<bool?>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<App>
            {
                Items = [
                    new App { Id = 21, Name = "App prueba", BankId = 11, Bank = new Bank { Id = 11, BranchId = 1 } },
                    new App { Id = 22, Name = "Otra sucursal", BankId = 12, Bank = new Bank { Id = 12, BranchId = 2 } },
                ],
                TotalCount = 2, Page = 1, PageSize = 100, TotalPages = 1,
            });
        repo.Setup(x => x.GetTotalAppPaymentsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(900m);
        repo.Setup(x => x.GetUnsettledAppPaymentsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(200m);
        repo.Setup(x => x.GetTotalAppPaymentsCountAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(4);
        repo.Setup(x => x.GetUnsettledAppPaymentsCountAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var mapper = new Mock<IMapper>();
        mapper.Setup(x => x.Map<AppDto>(It.IsAny<App>()))
            .Returns((App app) => new AppDto
            {
                Id = app.Id, Name = app.Name, BankId = app.BankId,
                BranchId = app.Bank.BranchId, Active = app.Active,
            });
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.Role).Returns("Cashier");
        var context = new Mock<IBranchContext>();
        context.Setup(x => x.ResolveOptional(It.IsAny<int?>())).Returns(1);
        var handler = new GetAppsHandler(repo.Object, mapper.Object, user.Object, context.Object);

        var picker = await handler.Handle(new GetAppsQuery
            { BranchId = 1, ForOrderCreation = true, PageSize = 100 }, CancellationToken.None);
        var selected = Assert.Single(picker.Items);
        Assert.Equal(21, selected.Id);
        Assert.Equal(1, picker.TotalCount);
        Assert.Equal(0m, selected.TotalPayments);
        Assert.Equal(0m, selected.UnsettledPayments);
        Assert.Equal(0, selected.TotalPaymentsCount);
        Assert.Equal(0, selected.UnsettledPaymentsCount);
        repo.Verify(x => x.GetTotalAppPaymentsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(x => x.GetUnsettledAppPaymentsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(x => x.GetTotalAppPaymentsCountAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(x => x.GetUnsettledAppPaymentsCountAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

        var admin = await handler.Handle(new GetAppsQuery { BranchId = 1 }, CancellationToken.None);
        var full = Assert.Single(admin.Items);
        Assert.Equal(21, full.Id);
        Assert.Equal(900m, full.TotalPayments);
        Assert.Equal(200m, full.UnsettledPayments);
        Assert.Equal(4, full.TotalPaymentsCount);
        Assert.Equal(1, full.UnsettledPaymentsCount);
        repo.Verify(x => x.GetTotalAppPaymentsAsync(21, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(x => x.GetUnsettledAppPaymentsAsync(21, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(x => x.GetTotalAppPaymentsCountAsync(21, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(x => x.GetUnsettledAppPaymentsCountAsync(21, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(x => x.GetTotalAppPaymentsAsync(22, It.IsAny<CancellationToken>()), Times.Never);
    }
}
