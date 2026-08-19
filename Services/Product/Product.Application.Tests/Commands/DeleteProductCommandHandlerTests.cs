using FluentAssertions;
using NSubstitute;
using Product.Application.Commands;
using Product.Application.Interfaces;
using Product.Domain.Entities;
using Product.Domain.Exceptions;

namespace Product.Application.Tests.Commands;

public class DeleteProductCommandHandlerTests
{
    private readonly IProductRepository _productRepository = Substitute.For<IProductRepository>();
    private readonly ICacheService _cacheService = Substitute.For<ICacheService>();
    private readonly DeleteProductCommandHandler _handler;

    public DeleteProductCommandHandlerTests()
    {
        _handler = new DeleteProductCommandHandler(_productRepository, _cacheService);
    }

    [Fact]
    public async Task Handle_WithValidCommand_ShouldDeactivateProduct()
    {
        var vendorId = Guid.NewGuid();
        var product = ProductEntity.Create(vendorId, "Widget", "desc", 9.99m, 10, Guid.NewGuid());
        var command = new DeleteProductCommand(product.Id, vendorId);

        _productRepository.GetByIdAsync(product.Id, default).Returns(product);

        await _handler.Handle(command, default);

        product.IsActive.Should().BeFalse();
        await _productRepository.Received(1).UpdateAsync(product, default);
    }

    [Fact]
    public async Task Handle_WhenProductNotFound_ShouldThrowNotFoundException()
    {
        var command = new DeleteProductCommand(Guid.NewGuid(), Guid.NewGuid());

        _productRepository.GetByIdAsync(command.Id, default).Returns((ProductEntity?)null);

        var act = async () => await _handler.Handle(command, default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WhenVendorDoesNotOwnProduct_ShouldThrowNotFoundException()
    {
        var product = ProductEntity.Create(Guid.NewGuid(), "Widget", "desc", 9.99m, 10, Guid.NewGuid());
        var command = new DeleteProductCommand(product.Id, Guid.NewGuid());

        _productRepository.GetByIdAsync(product.Id, default).Returns(product);

        var act = async () => await _handler.Handle(command, default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
