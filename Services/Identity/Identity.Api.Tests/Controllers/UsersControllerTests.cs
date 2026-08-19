using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Identity.Api.Tests.Fixtures;
using Identity.Domain.Entities;

namespace Identity.Api.Tests.Controllers;

public class UsersControllerTests : IClassFixture<IdentityApiFactory>
{
    private readonly HttpClient _client;
    private readonly IdentityApiFactory _factory;

    public UsersControllerTests(IdentityApiFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    // ── GET /api/users/me ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetMe_WithoutToken_ShouldReturn401()
    {
        var response = await _client.GetAsync("/api/users/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMe_WithValidToken_ShouldReturn200()
    {
        var user = ApplicationUser.Create("me@example.com", "Me User");
        _factory.UserRepository.Seed(user, "StrongP@ss1");

        var jwt = JwtTokenHelper.GenerateToken(user.Id, user.Email, "Customer");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.GetAsync("/api/users/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetMe_WithValidToken_ShouldReturn_UserProfile()
    {
        var user = ApplicationUser.Create("profile@example.com", "Profile User");
        _factory.UserRepository.Seed(user, "StrongP@ss1");

        var jwt = JwtTokenHelper.GenerateToken(user.Id, user.Email, "Customer");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.GetAsync("/api/users/me");
        var body     = await response.Content.ReadFromJsonAsync<UserProfileDto>();

        body!.Email.Should().Be("profile@example.com");
        body.DisplayName.Should().Be("Profile User");
        body.Role.Should().Be("Customer");
    }

    // ── POST /api/admin/users/{id}/assign-role ────────────────────────────────

    [Fact]
    public async Task AssignRole_AsCustomer_ShouldReturn403()
    {
        var user = ApplicationUser.Create("customer@example.com", "Customer");
        _factory.UserRepository.Seed(user, "StrongP@ss1");

        var jwt = JwtTokenHelper.GenerateToken(user.Id, user.Email, "Customer");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.PostAsJsonAsync(
            $"/api/admin/users/{user.Id}/assign-role",
            new { role = "Vendor" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AssignRole_AsAdmin_ShouldReturn200()
    {
        var admin  = ApplicationUser.Create("admin@example.com", "Admin");
        var target = ApplicationUser.Create("target@example.com", "Target");
        _factory.UserRepository.Seed(admin,  "AdminP@ss1");
        _factory.UserRepository.Seed(target, "TargetP@ss1");

        var jwt = JwtTokenHelper.GenerateToken(admin.Id, admin.Email, "Admin");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.PostAsJsonAsync(
            $"/api/admin/users/{target.Id}/assign-role",
            new { role = "Vendor" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AssignRole_WithoutToken_ShouldReturn401()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/admin/users/{Guid.NewGuid()}/assign-role",
            new { role = "Vendor" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /api/admin/users/{id}/reset-password ─────────────────────────────

    [Fact]
    public async Task ResetPassword_AsAdmin_ShouldReturn200()
    {
        var admin  = ApplicationUser.Create("admin2@example.com", "Admin");
        var target = ApplicationUser.Create("resettarget@example.com", "Target");
        _factory.UserRepository.Seed(admin,  "AdminP@ss1");
        _factory.UserRepository.Seed(target, "OldP@ss1");

        var jwt = JwtTokenHelper.GenerateToken(admin.Id, admin.Email, "Admin");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.PostAsJsonAsync(
            $"/api/admin/users/{target.Id}/reset-password",
            new { newPassword = "NewStrongP@ss1" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ResetPassword_AsAdmin_ShouldAllowLoginWithNewPassword()
    {
        var admin  = ApplicationUser.Create("admin3@example.com", "Admin");
        var target = ApplicationUser.Create("resetlogin@example.com", "Target");
        _factory.UserRepository.Seed(admin,  "AdminP@ss1");
        _factory.UserRepository.Seed(target, "OldP@ss1");

        var jwt = JwtTokenHelper.GenerateToken(admin.Id, admin.Email, "Admin");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        await _client.PostAsJsonAsync(
            $"/api/admin/users/{target.Id}/reset-password",
            new { newPassword = "NewStrongP@ss1" });

        _client.DefaultRequestHeaders.Authorization = null;
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email    = "resetlogin@example.com",
            password = "NewStrongP@ss1"
        });

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ResetPassword_AsCustomer_ShouldReturn403()
    {
        var user = ApplicationUser.Create("customer2@example.com", "Customer");
        _factory.UserRepository.Seed(user, "StrongP@ss1");

        var jwt = JwtTokenHelper.GenerateToken(user.Id, user.Email, "Customer");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.PostAsJsonAsync(
            $"/api/admin/users/{user.Id}/reset-password",
            new { newPassword = "NewStrongP@ss1" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ResetPassword_WithoutToken_ShouldReturn401()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/admin/users/{Guid.NewGuid()}/reset-password",
            new { newPassword = "NewStrongP@ss1" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ResetPassword_WithUnknownUser_ShouldReturn404()
    {
        var admin = ApplicationUser.Create("admin4@example.com", "Admin");
        _factory.UserRepository.Seed(admin, "AdminP@ss1");

        var jwt = JwtTokenHelper.GenerateToken(admin.Id, admin.Email, "Admin");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.PostAsJsonAsync(
            $"/api/admin/users/{Guid.NewGuid()}/reset-password",
            new { newPassword = "NewStrongP@ss1" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ResetPassword_WithWeakPassword_ShouldReturn400()
    {
        var admin  = ApplicationUser.Create("admin5@example.com", "Admin");
        var target = ApplicationUser.Create("weakpasstarget@example.com", "Target");
        _factory.UserRepository.Seed(admin,  "AdminP@ss1");
        _factory.UserRepository.Seed(target, "OldP@ss1");

        var jwt = JwtTokenHelper.GenerateToken(admin.Id, admin.Email, "Admin");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        var response = await _client.PostAsJsonAsync(
            $"/api/admin/users/{target.Id}/reset-password",
            new { newPassword = "weak" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

file record UserProfileDto(Guid Id, string Email, string DisplayName, string Role, bool IsEmailVerified);