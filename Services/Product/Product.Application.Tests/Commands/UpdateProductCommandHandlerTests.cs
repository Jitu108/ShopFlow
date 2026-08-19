using FluentAssertions;
using NSubstitute;
using Product.Application.Commands;
using Product.Application.Interfaces;
using Product.Domain.Entities;
using Product.Domain.Exceptions;

namespace Product.Application.Tests.Commands;

public class UpdateProductCommandHandlerTests
{
    private readonly IProductRepository _productRepository = Substitute.For<IProductRepository>();
    private readonly ICacheService _cacheService = Substitute.For<ICacheService>();
    private readonly UpdateProductCommandHandler _handler;

    public UpdateProductCommandHandlerTests()
    {
        _handler = new UpdateProductCommandHandler(_productRepository, _cacheService);
    }

    [Fact]
    public async Task Handle_WithValidCommand_ShouldUpdateAndReturnDto()
    {
        var vendorId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var product = ProductEntity.Create(vendorId, "Widget", "desc", 9.99m, 10, categoryId);
        var command = new UpdateProductCommand(product.Id, vendorId, "Gadget", "new desc", 19.99m, 5, categoryId);

        _productRepository.GetByIdAsync(product.Id, default).Returns(product);

        var result = await _handler.Handle(command, default);

        result.Name.Should().Be("Gadget");
        result.Price.Should().Be(19.99m);
    }

    [Fact]
    public async Task Handle_WhenProductNotFound_ShouldThrowNotFoundException()
    {
        var command = new UpdateProductCommand(Guid.NewGuid(), Guid.NewGuid(), "Gadget", "desc", 19.99m, 5, Guid.NewGuid());

        _productRepository.GetByIdAsync(command.Id, default).Returns((ProductEntity?)null);

        var act = async () => await _handler.Handle(command, default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WhenVendorDoesNotOwnProduct_ShouldThrowNotFoundException()
    {
        var product = ProductEntity.Create(Guid.NewGuid(), "Widget", "desc", 9.99m, 10, Guid.NewGuid());
        var command = new UpdateProductCommand(product.Id, Guid.NewGuid(), "Gadget", "desc", 19.99m, 5, Guid.NewGuid());

        _productRepository.GetByIdAsync(product.Id, default).Returns(product);

        var act = async () => await _handler.Handle(command, default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ShouldInvalidate_ProductAndCatalogCache()
    {
        var vendorId = Guid.NewGuid();
        var product = ProductEntity.Create(vendorId, "Widget", "desc", 9.99m, 10, Guid.NewGuid());
        var command = new UpdateProductCommand(product.Id, vendorId, "Gadget", "desc", 19.99m, 5, Guid.NewGuid());

        _productRepository.GetByIdAsync(product.Id, default).Returns(product);

        await _handler.Handle(command, default);

        await _cacheService.Received(1).RemoveAsync($"product:{product.Id}", default);
        await _cacheService.Received(1).RemoveAsync("product:catalog", default);
    }
}
