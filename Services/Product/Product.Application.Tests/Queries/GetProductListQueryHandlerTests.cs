using FluentAssertions;
using NSubstitute;
using Product.Application.DTOs;
using Product.Application.Interfaces;
using Product.Application.Queries;
using Product.Domain.Entities;

namespace Product.Application.Tests.Queries;

public class GetProductListQueryHandlerTests
{
    private readonly IProductRepository _productRepository = Substitute.For<IProductRepository>();
    private readonly ICacheService _cacheService = Substitute.For<ICacheService>();
    private readonly GetProductListQueryHandler _handler;

    public GetProductListQueryHandlerTests()
    {
        _handler = new GetProductListQueryHandler(_productRepository, _cacheService);
    }

    [Fact]
    public async Task Handle_WhenCacheHit_ShouldNotCallRepository()
    {
        var dtos = new List<ProductDto>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "Widget", "desc", 9.99m, 10, true, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow)
        };

        _cacheService.GetAsync<IReadOnlyList<ProductDto>>("product:catalog", default).Returns(dtos);

        var result = await _handler.Handle(new GetProductListQuery(), default);

        result.Should().BeEquivalentTo(dtos);
        await _productRepository.DidNotReceive().GetAllActiveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenCacheMiss_ShouldFetchFromRepositoryAndPopulateCache()
    {
        var product = ProductEntity.Create(Guid.NewGuid(), "Widget", "desc", 9.99m, 10, Guid.NewGuid());

        _cacheService.GetAsync<IReadOnlyList<ProductDto>>("product:catalog", default).Returns((IReadOnlyList<ProductDto>?)null);
        _productRepository.GetAllActiveAsync(default).Returns(new List<ProductEntity> { product });

        var result = await _handler.Handle(new GetProductListQuery(), default);

        result.Should().HaveCount(1);
        await _cacheService.Received(1).SetAsync("product:catalog", Arg.Any<IReadOnlyList<ProductDto>>(), Arg.Any<TimeSpan>(), default);
    }
}
