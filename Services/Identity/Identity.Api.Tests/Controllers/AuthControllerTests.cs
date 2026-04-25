using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Identity.Api.Tests.Fixtures;
using Identity.Domain.Entities;

namespace Identity.Api.Tests.Controllers;

public class AuthControllerTests : IClassFixture<IdentityApiFactory>
{
    private readonly HttpClient _client;
    private readonly IdentityApiFactory _factory;

    public AuthControllerTests(IdentityApiFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    // ── Register ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_WithValidData_ShouldReturn201()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email       = "newuser@example.com",
            password    = "StrongP@ss1",
            displayName = "New User"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Register_WithValidData_ShouldReturn_TokensInBody()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email       = "tokencheck@example.com",
            password    = "StrongP@ss1",
            displayName = "Token Check"
        });

        var body = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_ShouldReturn409()
    {
        var payload = new { email = "dup@example.com", password = "StrongP@ss1", displayName = "Dup" };
        await _client.PostAsJsonAsync("/api/auth/register", payload);

        var response = await _client.PostAsJsonAsync("/api/auth/register", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Register_WithInvalidData_ShouldReturn400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email       = "not-an-email",
            password    = "weak",
            displayName = ""
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Login ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_WithValidCredentials_ShouldReturn200()
    {
        var user = ApplicationUser.Create("login@example.com", "Login User");
        _factory.UserRepository.Seed(user, "StrongP@ss1");

        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email    = "login@example.com",
            password = "StrongP@ss1"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_WithValidCredentials_ShouldReturn_Tokens()
    {
        var user = ApplicationUser.Create("login2@example.com", "Login User 2");
        _factory.UserRepository.Seed(user, "StrongP@ss1");

        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email    = "login2@example.com",
            password = "StrongP@ss1"
        });

        var body = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_WithWrongPassword_ShouldReturn401()
    {
        var user = ApplicationUser.Create("wrongpass@example.com", "Wrong Pass");
        _factory.UserRepository.Seed(user, "CorrectP@ss1");

        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email    = "wrongpass@example.com",
            password = "WrongP@ss1"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ShouldReturn401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email    = "ghost@example.com",
            password = "StrongP@ss1"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_WithValidToken_ShouldReturn200()
    {
        // Register to obtain a valid refresh token
        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email       = "refresh@example.com",
            password    = "StrongP@ss1",
            displayName = "Refresh User"
        });
        var tokens = await registerResponse.Content.ReadFromJsonAsync<AuthResponseDto>();

        var response = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { token = tokens!.RefreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_WithInvalidToken_ShouldReturn401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/refresh",
            new { token = "completely-invalid-token" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Logout ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_WithValidToken_ShouldReturn204()
    {
        var user  = ApplicationUser.Create("logout@example.com", "Logout User");
        _factory.UserRepository.Seed(user, "StrongP@ss1");
        var jwt   = JwtTokenHelper.GenerateToken(user.Id, user.Email, "Customer");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "logout2@example.com", password = "StrongP@ss1", displayName = "Logout 2"
        });
        var tokens = await registerResponse.Content.ReadFromJsonAsync<AuthResponseDto>();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        var response = await _client.PostAsJsonAsync("/api/auth/logout",
            new { token = tokens.RefreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}

file record AuthResponseDto(string AccessToken, string RefreshToken, string Email, string DisplayName, string Role);