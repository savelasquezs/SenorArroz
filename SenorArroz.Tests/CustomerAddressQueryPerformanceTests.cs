using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Customers.Queries;
using SenorArroz.Application.Features.Customers.DTOs;
using SenorArroz.Application.Mappings;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Tests;

public sealed class CustomerAddressQueryPerformanceTests
{
    private static IMapper CreateMapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<CustomerMappingProfile>(),
            NullLoggerFactory.Instance).CreateMapper();

    [Fact]
    public async Task Existing_customer_checks_tenant_scoped_existence_without_hydrating_customer_and_phones()
    {
        using var cancellation = new CancellationTokenSource();
        var customers = new Mock<ICustomerRepository>(MockBehavior.Strict);
        customers.Setup(r => r.ExistsAsync(42, cancellation.Token)).ReturnsAsync(true);
        var addresses = new Mock<IAddressRepository>(MockBehavior.Strict);
        addresses.Setup(r => r.GetByCustomerIdAsync(42, cancellation.Token))
            .ReturnsAsync(new List<Address>
            {
                new()
                {
                    Id = 7, TenantId = 1, CustomerId = 42,
                    AddressText = "Calle de prueba 1", DeliveryFee = 4500,
                    BranchServices = new List<AddressBranch>
                    {
                        new() { TenantId = 1, BranchId = 2,
                            Branch = new Branch { Id = 2, TenantId = 1, Name = "Sucursal B" },
                            DeliveryFee = 5000, IsCovered = true }
                    }
                }
            }.AsEnumerable());
        var handler = new GetCustomerAddressesHandler(addresses.Object, customers.Object,
            CreateMapper(), Mock.Of<ICurrentUser>());

        var result = (await handler.Handle(new GetCustomerAddressesQuery { CustomerId = 42 },
            cancellation.Token)).ToList();

        var address = Assert.Single(result);
        Assert.Equal(7, address.Id);
        Assert.Equal(42, address.CustomerId);
        Assert.Equal("Calle de prueba 1", address.Address);
        Assert.Equal(4500, address.DeliveryFee);
        var service = Assert.Single(address.BranchServices);
        Assert.Equal(2, service.BranchId);
        Assert.Equal("Sucursal B", service.BranchName);
        customers.Verify(r => r.ExistsAsync(42, cancellation.Token), Times.Once);
        customers.Verify(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        addresses.Verify(r => r.GetByCustomerIdAsync(42, cancellation.Token), Times.Once);
    }

    [Fact]
    public async Task Missing_customer_preserves_business_error_and_skips_address_query()
    {
        using var cancellation = new CancellationTokenSource();
        var customers = new Mock<ICustomerRepository>(MockBehavior.Strict);
        customers.Setup(r => r.ExistsAsync(999, cancellation.Token)).ReturnsAsync(false);
        var addresses = new Mock<IAddressRepository>(MockBehavior.Strict);
        var handler = new GetCustomerAddressesHandler(addresses.Object, customers.Object,
            CreateMapper(), Mock.Of<ICurrentUser>());

        var error = await Assert.ThrowsAsync<BusinessException>(() =>
            handler.Handle(new GetCustomerAddressesQuery { CustomerId = 999 }, cancellation.Token));

        Assert.Equal("Cliente no encontrado", error.Message);
        customers.Verify(r => r.ExistsAsync(999, cancellation.Token), Times.Once);
        addresses.Verify(r => r.GetByCustomerIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Existing_customer_without_addresses_returns_empty_collection()
    {
        var customers = new Mock<ICustomerRepository>(MockBehavior.Strict);
        customers.Setup(r => r.ExistsAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var addresses = new Mock<IAddressRepository>(MockBehavior.Strict);
        addresses.Setup(r => r.GetByCustomerIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<Address>());
        var handler = new GetCustomerAddressesHandler(addresses.Object, customers.Object,
            CreateMapper(), Mock.Of<ICurrentUser>());

        var result = await handler.Handle(new GetCustomerAddressesQuery { CustomerId = 42 },
            CancellationToken.None);

        Assert.Empty(result);
    }
}
