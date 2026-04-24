using Identity.Domain.Entities;

namespace Identity.Application.Interfaces;

public interface IUserRepository
{
    Task<bool> ExistsByEmailAsync(string email, CancellationToken ct);
    Task<ApplicationUser> CreateAsync(ApplicationUser user, string password, CancellationToken ct);
    Task<ApplicationUser?> FindByEmailAsync(string email, CancellationToken ct);
    Task<ApplicationUser?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<bool> CheckPasswordAsync(ApplicationUser user, string password, CancellationToken ct);
    Task UpdateAsync(ApplicationUser user, CancellationToken ct);
}