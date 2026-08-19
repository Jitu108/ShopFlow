using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Product.Api.Tests.Fixtures;
using Product.Application.DTOs;
using Product.Domain.Entities;

namespace Product.Api.Tests.Controllers;

public class ProductsControllerTests : IClassFixture<ProductApiFactory>
{
    private readonly HttpClient _client;
    private readonly ProductApiFactory _factory;

    public ProductsControllerTests(ProductApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── GET /api/products ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_WithoutAuth_ShouldReturn200()
    {
        var response = await _client.GetAsync("/api/products");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_WithUnknownId_ShouldReturn404()
    {
        var response = await _client.GetAsync($"/api/products/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_WithKnownId_ShouldReturn200_AndProductBody()
    {
        var product = ProductEntity.Create(Guid.NewGuid(), "Widget", "desc", 9.99m, 10, Guid.NewGuid());
        _factory.ProductRepository.Seed(product);

        var response = await _client.GetAsync($"/api/products/{product.Id}");
        var body = await response.Content.ReadFromJsonAsync<ProductDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Name.Should().Be("Widget");
    }

    // ── POST /api/products ─────────────────────────────────────────────────────

    [Fact]
    public async Task Create_WithoutAuth_ShouldReturn401()
    {
        var response = await _client.PostAsJsonAsync("/api/products", new
        {
            name = "Widget",
            description = "desc",
            price = 9.99m,
            stockQuantity = 10,
            categoryId = Guid.NewGuid()
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_WithCustomerRole_ShouldReturn403()
    {
        var jwt = JwtTokenHelper.GenerateToken(Guid.NewGuid(), "customer@example.com", "Customer");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.PostAsJsonAsync("/api/products", new
        {
            name = "Widget",
            description = "desc",
            price = 9.99m,
            stockQuantity = 10,
            categoryId = Guid.NewGuid()
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Create_WithVendorRole_ShouldReturn201_AndCreatedProduct()
    {
        var vendorId = Guid.NewGuid();
        var jwt = JwtTokenHelper.GenerateToken(vendorId, "vendor@example.com", "Vendor");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.PostAsJsonAsync("/api/products", new
        {
            name = "Widget",
            description = "desc",
            price = 9.99m,
            stockQuantity = 10,
            categoryId = Guid.NewGuid()
        });
        var body = await response.Content.ReadFromJsonAsync<ProductDto>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        body!.VendorId.Should().Be(vendorId);
        _client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Create_WithInvalidBody_ShouldReturn400()
    {
        var jwt = JwtTokenHelper.GenerateToken(Guid.NewGuid(), "vendor@example.com", "Vendor");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.PostAsJsonAsync("/api/products", new
        {
            name = "",
            description = "desc",
            price = -1m,
            stockQuantity = -1,
            categoryId = Guid.Empty
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _client.DefaultRequestHeaders.Authorization = null;
    }

    // ── PUT / DELETE /api/products/{id} ────────────────────────────────────────

    [Fact]
    public async Task Update_AsOwningVendor_ShouldReturn200()
    {
        var vendorId = Guid.NewGuid();
        var product = ProductEntity.Create(vendorId, "Widget", "desc", 9.99m, 10, Guid.NewGuid());
        _factory.ProductRepository.Seed(product);

        var jwt = JwtTokenHelper.GenerateToken(vendorId, "vendor@example.com", "Vendor");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.PutAsJsonAsync($"/api/products/{product.Id}", new
        {
            name = "Gadget",
            description = "new desc",
            price = 19.99m,
            stockQuantity = 5,
            categoryId = product.CategoryId
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Update_AsNonOwningVendor_ShouldReturn404()
    {
        var product = ProductEntity.Create(Guid.NewGuid(), "Widget", "desc", 9.99m, 10, Guid.NewGuid());
        _factory.ProductRepository.Seed(product);

        var jwt = JwtTokenHelper.GenerateToken(Guid.NewGuid(), "other-vendor@example.com", "Vendor");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.PutAsJsonAsync($"/api/products/{product.Id}", new
        {
            name = "Gadget",
            description = "new desc",
            price = 19.99m,
            stockQuantity = 5,
            categoryId = product.CategoryId
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Delete_AsOwningVendor_ShouldReturn204()
    {
        var vendorId = Guid.NewGuid();
        var product = ProductEntity.Create(vendorId, "Widget", "desc", 9.99m, 10, Guid.NewGuid());
        _factory.ProductRepository.Seed(product);

        var jwt = JwtTokenHelper.GenerateToken(vendorId, "vendor@example.com", "Vendor");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.DeleteAsync($"/api/products/{product.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task Delete_WithoutAuth_ShouldReturn401()
    {
        var response = await _client.DeleteAsync($"/api/products/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
