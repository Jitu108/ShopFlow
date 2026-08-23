using Identity.Application.DTOs;
using Identity.Application.Interfaces;
using MediatR;

namespace Identity.Application.Queries;

public record SearchUsersByNameQuery(string? Name) : IRequest<IReadOnlyList<UserProfileDto>>;

public class SearchUsersByNameQueryHandler : IRequestHandler<SearchUsersByNameQuery, IReadOnlyList<UserProfileDto>>
{
    private readonly IUserRepository _userRepository;

    public SearchUsersByNameQueryHandler(IUserRepository userRepository)
        => _userRepository = userRepository;

    public async Task<IReadOnlyList<UserProfileDto>> Handle(SearchUsersByNameQuery query, CancellationToken ct)
    {
        var users = await _userRepository.SearchByNameAsync(query.Name, ct);
        return users
            .Select(u => new UserProfileDto(u.Id, u.Email, u.DisplayName, u.Role.ToString(), u.IsEmailVerified))
            .ToList();
    }
}
