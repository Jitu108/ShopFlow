using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Order.Api.Tests.Fixtures;
using Order.Application.DTOs;
using Order.Domain.Entities;

namespace Order.Api.Tests.Controllers;

public class AdminOrdersControllerTests : IClassFixture<OrderApiFactory>
{
    private readonly HttpClient _client;
    private readonly OrderApiFactory _factory;

    public AdminOrdersControllerTests(OrderApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private void AuthorizeAs(Guid userId, string email, string role)
        => _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JwtTokenHelper.GenerateToken(userId, email, role, emailVerified: true));

    [Fact]
    public async Task GetAll_WithoutAuth_ShouldReturn401()
    {
        var response = await _client.GetAsync("/api/admin/orders");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAll_AsNonAdmin_ShouldReturn403()
    {
        AuthorizeAs(Guid.NewGuid(), "customer@example.com", "Customer");

        var response = await _client.GetAsync("/api/admin/orders");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAll_AsAdmin_ShouldReturn200_AndAllOrders_AcrossCustomers()
    {
        var orderA = OrderEntity.Create(Guid.NewGuid(), "a@example.com",
            [OrderItemEntity.Create(Guid.NewGuid(), "Widget", 10m, 1)]);
        var orderB = OrderEntity.Create(Guid.NewGuid(), "b@example.com",
            [OrderItemEntity.Create(Guid.NewGuid(), "Gadget", 20m, 1)]);
        _factory.OrderRepository.Seed(orderA);
        _factory.OrderRepository.Seed(orderB);
        AuthorizeAs(Guid.NewGuid(), "admin@example.com", "Admin");

        var response = await _client.GetAsync("/api/admin/orders");
        var body = await response.Content.ReadFromJsonAsync<List<OrderDto>>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Should().HaveCount(2);
    }
}
