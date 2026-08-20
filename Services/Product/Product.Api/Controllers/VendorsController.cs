using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Product.Application.Queries;

namespace Product.Api.Controllers;

[ApiController]
[Route("api/vendors")]
public class VendorsController : ControllerBase
{
    private readonly IMediator _mediator;

    public VendorsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("{id:guid}/products")]
    [Authorize(Policy = "RequireVendor")]
    public async Task<IActionResult> GetVendorProducts(Guid id, CancellationToken ct)
    {
        if (id != VendorId)
        {
            return Forbid();
        }

        return Ok(await _mediator.Send(new GetVendorProductsQuery(id), ct));
    }

    private Guid VendorId => Guid.Parse(User.FindFirstValue("userId")!);
}
