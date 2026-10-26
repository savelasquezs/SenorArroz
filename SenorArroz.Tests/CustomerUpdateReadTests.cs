using AutoMapper;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Customers.Commands;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Tests;

public sealed class CustomerUpdateReadTests
{
    private static (UpdateCustomerHandler Handler, Mock<ICustomerRepository> Repository, Mock<IMapper> Mapper)
        Setup(Customer original, Customer updated, CancellationToken token)
    {
        var repo = new Mock<ICustomerRepository>();
        repo.Setup(r => r.GetByIdAsync(original.Id, token)).ReturnsAsync(original);
        repo.Setup(r => r.UpdateAsync(It.IsAny<Customer>(), token)).ReturnsAsync(updated);
        // A duplicate hydration succeeds too, but the regression must detect the redundant call.
        repo.Setup(r => r.GetByIdWithAddressesAsync(original.Id, token)).ReturnsAsync(updated);
        repo.Setup(r => r.GetOrderStatisticsForCustomersAsync(
                It.IsAny<IReadOnlyCollection<int>>(), token))
            .ReturnsAsync((IReadOnlyDictionary<int, CustomerOrderStatistics>)
                new Dictionary<int, CustomerOrderStatistics>
                {
                    [original.Id] = new(3, null, null, 45000, 2)
                });

        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<CustomerDto>(It.IsAny<Customer>()))
            .Returns((Customer entity) => new CustomerDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Active = entity.Active,
                Addresses = entity.Addresses.Select(address => new CustomerAddressDto
                {
                    Id = address.Id,
                }).ToList()
            });

        var loyalty = new Mock<ILoyaltyCycleService>();
        loyalty.Setup(s => s.ApplyLoyaltyPreviewsToCustomerDtosAsync(
                It.IsAny<IReadOnlyCollection<CustomerDto>>(), token))
            .Returns(Task.CompletedTask);
        var currentTenant = new Mock<ICurrentTenant>();
        currentTenant.SetupGet(t => t.TenantId).Returns(1);

        return (new UpdateCustomerHandler(repo.Object, mapper.Object, loyalty.Object, currentTenant.Object),
            repo, mapper);
    }

    private static Customer OldCustomer() => new()
    {
        Id = 42, TenantId = 1, BranchId = 1,
        Name = "Before", Phone1 = "3001111111", Phone2 = "3011111111", Active = true
    };

    private static Customer UpdatedCustomer() => new()
    {
        Id = 42, TenantId = 1, BranchId = 1,
        Name = "After", Phone1 = "3001111111", Phone2 = "3011111111", Active = true,
        Addresses = new List<Address> { new() { Id = 7, TenantId = 1, CustomerId = 42 } }
    };

    private static UpdateCustomerCommand Command(string? first = "3001111111", string? second = "3011111111") =>
        new() { Id = 42, Name = "After", Phone1 = first, Phone2 = second, Active = true };

    [Fact]
    public async Task Update_uses_already_hydrated_repository_result_without_a_second_address_read()
    {
        using var cancellation = new CancellationTokenSource();
        var (handler, repo, mapper) = Setup(OldCustomer(), UpdatedCustomer(), cancellation.Token);

        var response = await handler.Handle(Command(), cancellation.Token);

        Assert.Equal("After", response.Name);
        Assert.Equal(7, Assert.Single(response.Addresses).Id);
        Assert.Equal(3, response.TotalOrders);
        Assert.Equal(45000, response.TotalAccumulated);
        repo.Verify(r => r.GetByIdWithAddressesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.GetOrderStatisticsForCustomersAsync(
            It.Is<IReadOnlyCollection<int>>(ids => ids.Count == 1 && ids.Contains(42)), cancellation.Token), Times.Once);
        mapper.Verify(m => m.Map<CustomerDto>(It.IsAny<Customer>()), Times.Once);
    }

    [Fact]
    public async Task Editing_name_with_unchanged_phones_does_not_repeat_two_identity_queries()
    {
        using var cancellation = new CancellationTokenSource();
        var (handler, repo, _) = Setup(OldCustomer(), UpdatedCustomer(), cancellation.Token);
        await handler.Handle(Command(), cancellation.Token);

        repo.Verify(r => r.PhoneExistsAsync(It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Formatted_existing_mobile_number_is_not_rechecked()
    {
        using var cancellation = new CancellationTokenSource();
        var (handler, repo, _) = Setup(OldCustomer(), UpdatedCustomer(), cancellation.Token);

        await handler.Handle(Command("+57 300 111 1111", "3011111111"), cancellation.Token);

        repo.Verify(r => r.PhoneExistsAsync(It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Changed_mobile_is_checked_once_with_request_cancellation_token()
    {
        using var cancellation = new CancellationTokenSource();
        var (handler, repo, _) = Setup(OldCustomer(), UpdatedCustomer(), cancellation.Token);
        repo.Setup(r => r.PhoneExistsAsync("3022222222", 1, 42, cancellation.Token)).ReturnsAsync(false);

        await handler.Handle(Command("3022222222", "3011111111"), cancellation.Token);

        repo.Verify(r => r.PhoneExistsAsync("3022222222", 1, 42, cancellation.Token), Times.Once);
        repo.Verify(r => r.PhoneExistsAsync(It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.UpdateAsync(It.IsAny<Customer>(), cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task Existing_phone_of_another_customer_still_blocks_update()
    {
        using var cancellation = new CancellationTokenSource();
        var (handler, repo, _) = Setup(OldCustomer(), UpdatedCustomer(), cancellation.Token);
        repo.Setup(r => r.PhoneExistsAsync("3022222222", 1, 42, cancellation.Token)).ReturnsAsync(true);

        await Assert.ThrowsAsync<BusinessException>(() =>
            handler.Handle(Command("3022222222", "3011111111"), cancellation.Token));

        repo.Verify(r => r.UpdateAsync(It.IsAny<Customer>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
