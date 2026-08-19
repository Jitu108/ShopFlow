using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Product.Application.Commands;
using Product.Application.Queries;

namespace Product.Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProductsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _mediator.Send(new GetProductListQuery(), ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetProductByIdQuery(id), ct));

    [HttpPost]
    [Authorize(Policy = "RequireVendor")]
    public async Task<IActionResult> Create(CreateProductRequest request, CancellationToken ct)
    {
        var command = new CreateProductCommand(VendorId, request.Name, request.Description, request.Price, request.StockQuantity, request.CategoryId);
        var result = await _mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "RequireVendor")]
    public async Task<IActionResult> Update(Guid id, UpdateProductRequest request, CancellationToken ct)
    {
        var command = new UpdateProductCommand(id, VendorId, request.Name, request.Description, request.Price, request.StockQuantity, request.CategoryId);
        return Ok(await _mediator.Send(command, ct));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "RequireVendor")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeleteProductCommand(id, VendorId), ct);
        return NoContent();
    }

    private Guid VendorId => Guid.Parse(User.FindFirstValue("userId")!);
}

public record CreateProductRequest(string Name, string Description, decimal Price, int StockQuantity, Guid CategoryId);

public record UpdateProductRequest(string Name, string Description, decimal Price, int StockQuantity, Guid CategoryId);
