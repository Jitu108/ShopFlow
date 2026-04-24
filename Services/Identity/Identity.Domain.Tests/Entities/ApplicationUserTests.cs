using FluentAssertions;
using Identity.Domain.Entities;
using Identity.Domain.Enums;

namespace Identity.Domain.Tests.Entities;

public class ApplicationUserTests
{
    [Fact]
    public void NewUser_ShouldHave_CustomerRoleByDefault()
    {
        var user = ApplicationUser.Create("john@example.com", "John Doe");

        user.Role.Should().Be(UserRole.Customer);
    }

    [Fact]
    public void NewUser_ShouldHave_EmailVerifiedFalseByDefault()
    {
        var user = ApplicationUser.Create("john@example.com", "John Doe");

        user.IsEmailVerified.Should().BeFalse();
    }

    [Fact]
    public void NewUser_ShouldSet_EmailAndDisplayName()
    {
        var user = ApplicationUser.Create("john@example.com", "John Doe");

        user.Email.Should().Be("john@example.com");
        user.DisplayName.Should().Be("John Doe");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Create_WithBlankDisplayName_ShouldThrow(string? displayName)
    {
        var act = () => ApplicationUser.Create("john@example.com", displayName!);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*DisplayName*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Create_WithBlankEmail_ShouldThrow(string? email)
    {
        var act = () => ApplicationUser.Create(email!, "John Doe");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Email*");
    }

    [Fact]
    public void VerifyEmail_ShouldSet_IsEmailVerifiedTrue()
    {
        var user = ApplicationUser.Create("john@example.com", "John Doe");

        user.VerifyEmail();

        user.IsEmailVerified.Should().BeTrue();
    }

    [Fact]
    public void UpdateProfile_ShouldChange_DisplayName()
    {
        var user = ApplicationUser.Create("john@example.com", "John Doe");

        user.UpdateProfile("Johnny Doe");

        user.DisplayName.Should().Be("Johnny Doe");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void UpdateProfile_WithBlankDisplayName_ShouldThrow(string? displayName)
    {
        var user = ApplicationUser.Create("john@example.com", "John Doe");

        var act = () => user.UpdateProfile(displayName!);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*DisplayName*");
    }

    [Fact]
    public void AssignRole_ShouldChange_UserRole()
    {
        var user = ApplicationUser.Create("john@example.com", "John Doe");

        user.AssignRole(UserRole.Vendor);

        user.Role.Should().Be(UserRole.Vendor);
    }
}
