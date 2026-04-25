using Identity.Application.Interfaces;
using Identity.Domain.Entities;

namespace Identity.Infrastructure.Persistence.Repositories;

// Full implementation requires ASP.NET Core Identity wiring (UserManager<T>).
// Registered in DI but replaced with FakeUserRepository in tests.
public class UserRepository : IUserRepository
{
    public Task<bool> ExistsByEmailAsync(string email, CancellationToken ct)
        => throw new NotImplementedException("Wire up ASP.NET Core Identity UserManager.");

    public Task<ApplicationUser> CreateAsync(ApplicationUser user, string password, CancellationToken ct)
        => throw new NotImplementedException("Wire up ASP.NET Core Identity UserManager.");

    public Task<ApplicationUser?> FindByEmailAsync(string email, CancellationToken ct)
        => throw new NotImplementedException("Wire up ASP.NET Core Identity UserManager.");

    public Task<ApplicationUser?> GetByIdAsync(Guid id, CancellationToken ct)
        => throw new NotImplementedException("Wire up ASP.NET Core Identity UserManager.");

    public Task<bool> CheckPasswordAsync(ApplicationUser user, string password, CancellationToken ct)
        => throw new NotImplementedException("Wire up ASP.NET Core Identity UserManager.");

    public Task UpdateAsync(ApplicationUser user, CancellationToken ct)
        => throw new NotImplementedException("Wire up ASP.NET Core Identity UserManager.");
}