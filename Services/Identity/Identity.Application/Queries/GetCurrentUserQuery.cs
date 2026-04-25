using Identity.Application.DTOs;
using Identity.Application.Interfaces;
using Identity.Domain.Entities;
using Identity.Domain.Exceptions;
using MediatR;

namespace Identity.Application.Queries;

public record GetCurrentUserQuery(Guid UserId) : IRequest<UserProfileDto>;

public class GetCurrentUserQueryHandler : IRequestHandler<GetCurrentUserQuery, UserProfileDto>
{
    private readonly IUserRepository _userRepository;

    public GetCurrentUserQueryHandler(IUserRepository userRepository)
        => _userRepository = userRepository;

    public async Task<UserProfileDto> Handle(GetCurrentUserQuery query, CancellationToken ct)
    {
        var user = await _userRepository.GetByIdAsync(query.UserId, ct)
            ?? throw new NotFoundException(nameof(ApplicationUser), query.UserId);

        return new UserProfileDto(user.Id, user.Email, user.DisplayName, user.Role.ToString(), user.IsEmailVerified);
    }
}