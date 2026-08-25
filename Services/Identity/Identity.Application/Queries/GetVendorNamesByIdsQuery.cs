using Identity.Application.DTOs;
using Identity.Application.Interfaces;
using MediatR;

namespace Identity.Application.Queries;

public record GetVendorNamesByIdsQuery(IReadOnlyList<Guid> Ids) : IRequest<IReadOnlyList<VendorSummaryDto>>;

public class GetVendorNamesByIdsQueryHandler : IRequestHandler<GetVendorNamesByIdsQuery, IReadOnlyList<VendorSummaryDto>>
{
    private readonly IUserRepository _userRepository;

    public GetVendorNamesByIdsQueryHandler(IUserRepository userRepository)
        => _userRepository = userRepository;

    public async Task<IReadOnlyList<VendorSummaryDto>> Handle(GetVendorNamesByIdsQuery query, CancellationToken ct)
    {
        if (query.Ids.Count == 0)
        {
            return [];
        }

        var vendors = await _userRepository.GetVendorsByIdsAsync(query.Ids, ct);
        return vendors
            .Select(v => new VendorSummaryDto(v.Id, v.DisplayName))
            .ToList();
    }
}
