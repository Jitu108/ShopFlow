using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Cart.Api.Tests.Fixtures;
using Cart.Application.DTOs;
using FluentAssertions;

namespace Cart.Api.Tests.Controllers;

public class CartControllerTests : IClassFixture<CartApiFactory>
{
    private readonly HttpClient _client;
    private readonly CartApiFactory _factory;

    public CartControllerTests(CartApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private void Authenticate(Guid userId)
    {
        var jwt = JwtTokenHelper.GenerateToken(userId, "customer@example.com", "Customer");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
    }

    // ── GET /api/cart ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCart_WithoutAuth_ShouldReturn401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/api/cart");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCart_WithEmptyCart_ShouldReturn200_AndEmptyList()
    {
        Authenticate(Guid.NewGuid());

        var response = await _client.GetAsync("/api/cart");
        var body = await response.Content.ReadFromJsonAsync<List<CartItemDto>>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().BeEmpty();
    }

    // ── POST /api/cart/items ────────────────────────────────────────────────────

    [Fact]
    public async Task AddItem_WithoutAuth_ShouldReturn401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PostAsJsonAsync("/api/cart/items", new
        {
            productId = Guid.NewGuid(),
            productName = "Widget",
            unitPrice = 9.99m,
            quantity = 1
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AddItem_WithValidBody_ShouldReturn201_AndItemBody()
    {
        Authenticate(Guid.NewGuid());
        var productId = Guid.NewGuid();

        var response = await _client.PostAsJsonAsync("/api/cart/items", new
        {
            productId,
            productName = "Widget",
            unitPrice = 9.99m,
            quantity = 2
        });
        var body = await response.Content.ReadFromJsonAsync<CartItemDto>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        body!.ProductId.Should().Be(productId);
        body.Quantity.Should().Be(2);
    }

    [Fact]
    public async Task AddItem_WithZeroQuantity_ShouldReturn400()
    {
        Authenticate(Guid.NewGuid());

        var response = await _client.PostAsJsonAsync("/api/cart/items", new
        {
            productId = Guid.NewGuid(),
            productName = "Widget",
            unitPrice = 9.99m,
            quantity = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── PUT /api/cart/items/{productId} ────────────────────────────────────────

    [Fact]
    public async Task UpdateItem_WithExistingProduct_ShouldReturn200_AndUpdatedQuantity()
    {
        var userId = Guid.NewGuid();
        Authenticate(userId);
        var productId = Guid.NewGuid();
        await _client.PostAsJsonAsync("/api/cart/items", new
        {
            productId,
            productName = "Widget",
            unitPrice = 9.99m,
            quantity = 1
        });

        var response = await _client.PutAsJsonAsync($"/api/cart/items/{productId}", new { quantity = 9 });
        var body = await response.Content.ReadFromJsonAsync<CartItemDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Quantity.Should().Be(9);
    }

    [Fact]
    public async Task UpdateItem_WithUnknownProduct_ShouldReturn404()
    {
        Authenticate(Guid.NewGuid());

        var response = await _client.PutAsJsonAsync($"/api/cart/items/{Guid.NewGuid()}", new { quantity = 3 });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── DELETE /api/cart/items/{productId} ─────────────────────────────────────

    [Fact]
    public async Task RemoveItem_WithExistingProduct_ShouldReturn204()
    {
        var userId = Guid.NewGuid();
        Authenticate(userId);
        var productId = Guid.NewGuid();
        await _client.PostAsJsonAsync("/api/cart/items", new
        {
            productId,
            productName = "Widget",
            unitPrice = 9.99m,
            quantity = 1
        });

        var response = await _client.DeleteAsync($"/api/cart/items/{productId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RemoveItem_WithUnknownProduct_ShouldReturn204_Idempotently()
    {
        Authenticate(Guid.NewGuid());

        var response = await _client.DeleteAsync($"/api/cart/items/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── DELETE /api/cart ────────────────────────────────────────────────────────

    [Fact]
    public async Task ClearCart_ShouldReturn204()
    {
        var userId = Guid.NewGuid();
        Authenticate(userId);
        await _client.PostAsJsonAsync("/api/cart/items", new
        {
            productId = Guid.NewGuid(),
            productName = "Widget",
            unitPrice = 9.99m,
            quantity = 1
        });

        var response = await _client.DeleteAsync("/api/cart");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
