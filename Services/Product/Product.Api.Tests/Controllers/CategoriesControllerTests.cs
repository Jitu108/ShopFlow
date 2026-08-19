using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Product.Api.Tests.Fixtures;
using Product.Application.DTOs;
using Product.Domain.Entities;

namespace Product.Api.Tests.Controllers;

public class CategoriesControllerTests : IClassFixture<ProductApiFactory>
{
    private readonly HttpClient _client;
    private readonly ProductApiFactory _factory;

    public CategoriesControllerTests(ProductApiFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    // ── GET /api/categories ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_WithoutAuth_ShouldReturn200()
    {
        var response = await _client.GetAsync("/api/categories");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAll_ShouldReturn_SeededCategories()
    {
        var category = Category.Create("GetAllTestCategory");
        _factory.CategoryRepository.Seed(category);

        var response = await _client.GetAsync("/api/categories");
        var body     = await response.Content.ReadFromJsonAsync<List<CategoryDto>>();

        body!.Should().Contain(c => c.Id == category.Id && c.Name == "GetAllTestCategory");
    }

    // ── POST /api/categories ────────────────────────────────────────────────────

    [Fact]
    public async Task Create_WithoutAuth_ShouldReturn401()
    {
        var response = await _client.PostAsJsonAsync("/api/categories", new { name = "Books" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_WithVendorRole_ShouldReturn403()
    {
        var jwt = JwtTokenHelper.GenerateToken(Guid.NewGuid(), "vendor@example.com", "Vendor");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.PostAsJsonAsync("/api/categories", new { name = "Books" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_AsAdmin_ShouldReturn201()
    {
        var jwt = JwtTokenHelper.GenerateToken(Guid.NewGuid(), "admin@example.com", "Admin");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.PostAsJsonAsync("/api/categories", new { name = "CreateAsAdminTestCategory" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Create_WithDuplicateName_ShouldReturn400()
    {
        _factory.CategoryRepository.Seed(Category.Create("DuplicateTestCategory"));

        var jwt = JwtTokenHelper.GenerateToken(Guid.NewGuid(), "admin2@example.com", "Admin");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.PostAsJsonAsync("/api/categories", new { name = "DuplicateTestCategory" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_WithBlankName_ShouldReturn400()
    {
        var jwt = JwtTokenHelper.GenerateToken(Guid.NewGuid(), "admin3@example.com", "Admin");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.PostAsJsonAsync("/api/categories", new { name = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
