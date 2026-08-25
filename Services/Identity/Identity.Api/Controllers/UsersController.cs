using System.Security.Claims;
using Identity.Application.Commands;
using Identity.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Identity.Api.Controllers;

[ApiController]
[Route("api")]
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsersController(IMediator mediator) => _mediator = mediator;

    [HttpGet("users/me")]
    [Authorize]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue("userId")!);
        return Ok(await _mediator.Send(new GetCurrentUserQuery(userId), ct));
    }

    [HttpGet("admin/users")]
    [Authorize(Policy = "RequireAdmin")]
    public async Task<IActionResult> SearchUsers([FromQuery] string? name, CancellationToken ct)
        => Ok(await _mediator.Send(new SearchUsersByNameQuery(name), ct));

    // Public and unauthenticated on purpose — the storefront catalog (which
    // anonymous shoppers browse) needs vendor display names. Only ever
    // returns id + displayName for Vendor-role users, never email/role/etc.
    [HttpGet("vendors")]
    public async Task<IActionResult> GetVendorNames([FromQuery] string ids, CancellationToken ct)
    {
        var parsedIds = ids
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => Guid.TryParse(id, out var parsed) ? parsed : (Guid?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        return Ok(await _mediator.Send(new GetVendorNamesByIdsQuery(parsedIds), ct));
    }

    [HttpPost("admin/users/{id}/assign-role")]
    [Authorize(Policy = "RequireAdmin")]
    public async Task<IActionResult> AssignRole(Guid id, AssignRoleRequest request, CancellationToken ct)
    {
        await _mediator.Send(new AssignRoleCommand(id, request.Role), ct);
        return Ok();
    }

    [HttpPost("admin/users/{id}/reset-password")]
    [Authorize(Policy = "RequireAdmin")]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        await _mediator.Send(new ResetPasswordCommand(id, request.NewPassword), ct);
        return Ok();
    }
}

public record AssignRoleRequest(string Role);
public record ResetPasswordRequest(string NewPassword);