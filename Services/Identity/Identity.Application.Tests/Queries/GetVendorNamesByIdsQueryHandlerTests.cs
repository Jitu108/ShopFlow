using FluentAssertions;
using Identity.Application.Interfaces;
using Identity.Application.Queries;
using Identity.Domain.Entities;
using Identity.Domain.Enums;
using NSubstitute;

namespace Identity.Application.Tests.Queries;

public class GetVendorNamesByIdsQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly GetVendorNamesByIdsQueryHandler _handler;

    public GetVendorNamesByIdsQueryHandlerTests()
    {
        _handler = new GetVendorNamesByIdsQueryHandler(_userRepository);
    }

    [Fact]
    public async Task Handle_WithKnownVendorIds_ShouldReturnIdAndDisplayNameOnly()
    {
        var vendor = ApplicationUser.Create("vendor@example.com", "Terra & Co.");
        vendor.AssignRole(UserRole.Vendor);
        var query = new GetVendorNamesByIdsQuery([vendor.Id]);

        _userRepository.GetVendorsByIdsAsync(query.Ids, default).Returns([vendor]);

        var result = await _handler.Handle(query, default);

        result.Should().ContainSingle();
        result[0].Id.Should().Be(vendor.Id);
        result[0].DisplayName.Should().Be("Terra & Co.");
    }

    [Fact]
    public async Task Handle_WithNoIds_ShouldReturnEmptyWithoutCallingRepository()
    {
        var query = new GetVendorNamesByIdsQuery([]);

        var result = await _handler.Handle(query, default);

        result.Should().BeEmpty();
        await _userRepository.DidNotReceive().GetVendorsByIdsAsync(Arg.Any<IReadOnlyList<Guid>>(), default);
    }
}
