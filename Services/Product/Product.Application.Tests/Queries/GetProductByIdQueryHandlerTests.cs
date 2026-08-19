using FluentAssertions;
using NSubstitute;
using Product.Application.DTOs;
using Product.Application.Interfaces;
using Product.Application.Queries;
using Product.Domain.Entities;
using Product.Domain.Exceptions;

namespace Product.Application.Tests.Queries;

public class GetProductByIdQueryHandlerTests
{
    private readonly IProductRepository _productRepository = Substitute.For<IProductRepository>();
    private readonly ICacheService _cacheService = Substitute.For<ICacheService>();
    private readonly GetProductByIdQueryHandler _handler;

    public GetProductByIdQueryHandlerTests()
    {
        _handler = new GetProductByIdQueryHandler(_productRepository, _cacheService);
    }

    [Fact]
    public async Task Handle_WhenCacheHit_ShouldNotCallRepository()
    {
        var productId = Guid.NewGuid();
        var dto = new ProductDto(productId, Guid.NewGuid(), "Widget", "desc", 9.99m, 10, true, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow);

        _cacheService.GetAsync<ProductDto>($"product:{productId}", default).Returns(dto);

        var result = await _handler.Handle(new GetProductByIdQuery(productId), default);

        result.Should().BeEquivalentTo(dto);
        await _productRepository.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenCacheMiss_ShouldFetchFromRepositoryAndPopulateCache()
    {
        var product = ProductEntity.Create(Guid.NewGuid(), "Widget", "desc", 9.99m, 10, Guid.NewGuid());

        _cacheService.GetAsync<ProductDto>($"product:{product.Id}", default).Returns((ProductDto?)null);
        _productRepository.GetByIdAsync(product.Id, default).Returns(product);

        var result = await _handler.Handle(new GetProductByIdQuery(product.Id), default);

        result.Name.Should().Be("Widget");
        await _cacheService.Received(1).SetAsync($"product:{product.Id}", Arg.Any<ProductDto>(), Arg.Any<TimeSpan>(), default);
    }

    [Fact]
    public async Task Handle_WhenProductDoesNotExist_ShouldThrowNotFoundException()
    {
        var productId = Guid.NewGuid();

        _cacheService.GetAsync<ProductDto>($"product:{productId}", default).Returns((ProductDto?)null);
        _productRepository.GetByIdAsync(productId, default).Returns((ProductEntity?)null);

        var act = async () => await _handler.Handle(new GetProductByIdQuery(productId), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
