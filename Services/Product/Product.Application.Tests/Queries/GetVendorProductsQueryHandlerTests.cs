using FluentAssertions;
using NSubstitute;
using Product.Application.Interfaces;
using Product.Application.Queries;
using Product.Domain.Entities;

namespace Product.Application.Tests.Queries;

public class GetVendorProductsQueryHandlerTests
{
    private readonly IProductRepository _productRepository = Substitute.For<IProductRepository>();
    private readonly GetVendorProductsQueryHandler _handler;

    public GetVendorProductsQueryHandlerTests()
    {
        _handler = new GetVendorProductsQueryHandler(_productRepository);
    }

    [Fact]
    public async Task Handle_ShouldReturn_OnlyVendorsProducts()
    {
        var vendorId = Guid.NewGuid();
        var product = ProductEntity.Create(vendorId, "Widget", "desc", 9.99m, 10, Guid.NewGuid());

        _productRepository.GetByVendorIdAsync(vendorId, default).Returns(new List<ProductEntity> { product });

        var result = await _handler.Handle(new GetVendorProductsQuery(vendorId), default);

        result.Should().ContainSingle().Which.VendorId.Should().Be(vendorId);
    }
}
