using Identity.Application.Interfaces;
using Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<ApplicationUser> _hasher;

    public UserRepository(AppDbContext db, IPasswordHasher<ApplicationUser> hasher)
    {
        _db     = db;
        _hasher = hasher;
    }

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken ct)
        => _db.Users.AnyAsync(u => u.Email == email.Trim(), ct);

    public async Task<ApplicationUser> CreateAsync(ApplicationUser user, string password, CancellationToken ct)
    {
        user.SetPasswordHash(_hasher.HashPassword(user, password));
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return user;
    }

    public Task<ApplicationUser?> FindByEmailAsync(string email, CancellationToken ct)
        => _db.Users.FirstOrDefaultAsync(u => u.Email == email.Trim(), ct);

    public Task<ApplicationUser?> GetByIdAsync(Guid id, CancellationToken ct)
        => _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<bool> CheckPasswordAsync(ApplicationUser user, string password, CancellationToken ct)
    {
        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return Task.FromResult(result != PasswordVerificationResult.Failed);
    }

    public async Task UpdateAsync(ApplicationUser user, CancellationToken ct)
    {
        _db.Users.Update(user);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ApplicationUser>> SearchByNameAsync(string? name, CancellationToken ct)
        => await _db.Users
            .Where(u => string.IsNullOrWhiteSpace(name) || u.DisplayName.Contains(name))
            .OrderBy(u => u.DisplayName)
            .ToListAsync(ct);

    public async Task ResetPasswordAsync(ApplicationUser user, string newPassword, CancellationToken ct)
    {
        user.SetPasswordHash(_hasher.HashPassword(user, newPassword));
        _db.Users.Update(user);
        await _db.SaveChangesAsync(ct);
    }
}
