using Identity.Application.Interfaces;
using Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _context;

    public RefreshTokenRepository(AppDbContext context) => _context = context;

    public async Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken ct)
        => await _context.RefreshTokens
            .SingleOrDefaultAsync(x => x.Token == token, ct);

    public async Task SaveAsync(RefreshToken token, CancellationToken ct)
    {
        await _context.RefreshTokens.AddAsync(token, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task RevokeAsync(string token, CancellationToken ct)
    {
        var existing = await _context.RefreshTokens
            .SingleOrDefaultAsync(x => x.Token == token, ct);

        if (existing is not null)
        {
            _context.RefreshTokens.Remove(existing);
            await _context.SaveChangesAsync(ct);
        }
    }
}