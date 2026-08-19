using FluentAssertions;
using NSubstitute;
using Product.Application.Commands;
using Product.Application.Interfaces;
using Product.Domain.Entities;

namespace Product.Application.Tests.Commands;

public class CreateProductCommandHandlerTests
{
    private readonly IProductRepository _productRepository = Substitute.For<IProductRepository>();
    private readonly ICacheService _cacheService = Substitute.For<ICacheService>();
    private readonly CreateProductCommandHandler _handler;

    public CreateProductCommandHandlerTests()
    {
        _handler = new CreateProductCommandHandler(_productRepository, _cacheService);
    }

    [Fact]
    public async Task Handle_WithValidCommand_ShouldReturnProductDto()
    {
        var command = new CreateProductCommand(Guid.NewGuid(), "Widget", "desc", 9.99m, 10, Guid.NewGuid());

        var result = await _handler.Handle(command, default);

        result.Name.Should().Be("Widget");
        result.Price.Should().Be(9.99m);
        result.VendorId.Should().Be(command.VendorId);
    }

    [Fact]
    public async Task Handle_ShouldCall_AddAsync_Once()
    {
        var command = new CreateProductCommand(Guid.NewGuid(), "Widget", "desc", 9.99m, 10, Guid.NewGuid());

        await _handler.Handle(command, default);

        await _productRepository.Received(1).AddAsync(Arg.Any<ProductEntity>(), default);
    }

    [Fact]
    public async Task Handle_ShouldInvalidate_CatalogCache()
    {
        var command = new CreateProductCommand(Guid.NewGuid(), "Widget", "desc", 9.99m, 10, Guid.NewGuid());

        await _handler.Handle(command, default);

        await _cacheService.Received(1).RemoveAsync("product:catalog", default);
    }
}
