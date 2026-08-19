using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Product.Api.Tests.Fixtures;
using Product.Domain.Entities;

namespace Product.Api.Tests.Controllers;

public class VendorsControllerTests : IClassFixture<ProductApiFactory>
{
    private readonly HttpClient _client;
    private readonly ProductApiFactory _factory;

    public VendorsControllerTests(ProductApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetVendorProducts_WithoutAuth_ShouldReturn401()
    {
        var response = await _client.GetAsync($"/api/vendors/{Guid.NewGuid()}/products");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetVendorProducts_AsVendor_ShouldReturn200_AndOnlyOwnProducts()
    {
        var vendorId = Guid.NewGuid();
        var ownProduct = ProductEntity.Create(vendorId, "Widget", "desc", 9.99m, 10, Guid.NewGuid());
        var otherProduct = ProductEntity.Create(Guid.NewGuid(), "Gadget", "desc", 19.99m, 5, Guid.NewGuid());
        _factory.ProductRepository.Seed(ownProduct);
        _factory.ProductRepository.Seed(otherProduct);

        var jwt = JwtTokenHelper.GenerateToken(vendorId, "vendor@example.com", "Vendor");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.GetAsync($"/api/vendors/{vendorId}/products");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _client.DefaultRequestHeaders.Authorization = null;
    }
}
