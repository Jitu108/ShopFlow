using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Order.Api.Tests.Fixtures;
using Order.Application.DTOs;
using Order.Domain.Entities;
using ShopFlow.Shared.Events;

namespace Order.Api.Tests.Controllers;

public class OrdersControllerTests : IClassFixture<OrderApiFactory>
{
    private readonly HttpClient _client;
    private readonly OrderApiFactory _factory;

    public OrdersControllerTests(OrderApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private void AuthorizeAs(Guid userId, string email, string role, bool emailVerified = true)
        => _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JwtTokenHelper.GenerateToken(userId, email, role, emailVerified));

    private static object ValidPlaceOrderBody() => new
    {
        items = new[]
        {
            new { productId = Guid.NewGuid(), productName = "Widget", unitPrice = 10.00m, quantity = 2 }
        }
    };

    // ── POST /api/orders ────────────────────────────────────────────────────────

    [Fact]
    public async Task PlaceOrder_WithoutAuth_ShouldReturn401()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", ValidPlaceOrderBody());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PlaceOrder_WithUnverifiedEmail_ShouldReturn403()
    {
        AuthorizeAs(Guid.NewGuid(), "customer@example.com", "Customer", emailVerified: false);

        var response = await _client.PostAsJsonAsync("/api/orders", ValidPlaceOrderBody());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PlaceOrder_WithValidRequest_ShouldReturn201_AndBody()
    {
        AuthorizeAs(Guid.NewGuid(), "customer@example.com", "Customer");

        var response = await _client.PostAsJsonAsync("/api/orders", ValidPlaceOrderBody());
        var body = await response.Content.ReadFromJsonAsync<OrderDto>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        body!.CustomerEmail.Should().Be("customer@example.com");
        body.TotalAmount.Should().Be(20.00m);
    }

    [Fact]
    public async Task PlaceOrder_WithEmptyItems_ShouldReturn400()
    {
        AuthorizeAs(Guid.NewGuid(), "customer@example.com", "Customer");

        var response = await _client.PostAsJsonAsync("/api/orders", new { items = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── GET /api/orders ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyOrders_WithoutAuth_ShouldReturn401()
    {
        var response = await _client.GetAsync("/api/orders");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMyOrders_ShouldReturn_OnlyOwnOrders()
    {
        var customerId = Guid.NewGuid();
        var ownOrder = OrderEntity.Create(customerId, "me@example.com",
            [OrderItemEntity.Create(Guid.NewGuid(), "Widget", 10m, 1)]);
        var otherOrder = OrderEntity.Create(Guid.NewGuid(), "other@example.com",
            [OrderItemEntity.Create(Guid.NewGuid(), "Gadget", 20m, 1)]);
        _factory.OrderRepository.Seed(ownOrder);
        _factory.OrderRepository.Seed(otherOrder);
        AuthorizeAs(customerId, "me@example.com", "Customer");

        var response = await _client.GetAsync("/api/orders");
        var body = await response.Content.ReadFromJsonAsync<List<OrderDto>>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Should().ContainSingle(o => o.Id == ownOrder.Id);
    }

    // ── GET /api/orders/{id} ────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_WithoutAuth_ShouldReturn401()
    {
        var response = await _client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetById_WithUnknownId_ShouldReturn404()
    {
        AuthorizeAs(Guid.NewGuid(), "customer@example.com", "Customer");

        var response = await _client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetById_AsOwner_ShouldReturn200()
    {
        var customerId = Guid.NewGuid();
        var order = OrderEntity.Create(customerId, "me@example.com",
            [OrderItemEntity.Create(Guid.NewGuid(), "Widget", 10m, 1)]);
        _factory.OrderRepository.Seed(order);
        AuthorizeAs(customerId, "me@example.com", "Customer");

        var response = await _client.GetAsync($"/api/orders/{order.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_AsNonOwner_ShouldReturn404()
    {
        var order = OrderEntity.Create(Guid.NewGuid(), "owner@example.com",
            [OrderItemEntity.Create(Guid.NewGuid(), "Widget", 10m, 1)]);
        _factory.OrderRepository.Seed(order);
        AuthorizeAs(Guid.NewGuid(), "intruder@example.com", "Customer");

        var response = await _client.GetAsync($"/api/orders/{order.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── PUT /api/orders/{id}/confirm ────────────────────────────────────────────

    [Fact]
    public async Task Confirm_WithoutAuth_ShouldReturn401()
    {
        var response = await _client.PutAsync($"/api/orders/{Guid.NewGuid()}/confirm", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Confirm_AsOwner_ShouldReturn200_AndPublishOrderPlacedEvent()
    {
        var customerId = Guid.NewGuid();
        var order = OrderEntity.Create(customerId, "me@example.com",
            [OrderItemEntity.Create(Guid.NewGuid(), "Widget", 10m, 1)]);
        _factory.OrderRepository.Seed(order);
        AuthorizeAs(customerId, "me@example.com", "Customer");

        var response = await _client.PutAsync($"/api/orders/{order.Id}/confirm", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var harness = _factory.Services.GetRequiredService<ITestHarness>();
        (await harness.Published.Any<OrderPlacedEvent>(x => x.Context.Message.OrderId == order.Id))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Confirm_WhenAlreadyConfirmed_ShouldReturn400()
    {
        var customerId = Guid.NewGuid();
        var order = OrderEntity.Create(customerId, "me@example.com",
            [OrderItemEntity.Create(Guid.NewGuid(), "Widget", 10m, 1)]);
        order.Confirm();
        _factory.OrderRepository.Seed(order);
        AuthorizeAs(customerId, "me@example.com", "Customer");

        var response = await _client.PutAsync($"/api/orders/{order.Id}/confirm", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Confirm_AsNonOwner_ShouldReturn404()
    {
        var order = OrderEntity.Create(Guid.NewGuid(), "owner@example.com",
            [OrderItemEntity.Create(Guid.NewGuid(), "Widget", 10m, 1)]);
        _factory.OrderRepository.Seed(order);
        AuthorizeAs(Guid.NewGuid(), "intruder@example.com", "Customer");

        var response = await _client.PutAsync($"/api/orders/{order.Id}/confirm", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
