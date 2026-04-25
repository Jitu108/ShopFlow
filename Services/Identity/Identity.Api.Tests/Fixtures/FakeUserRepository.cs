using Identity.Application.Interfaces;
using Identity.Domain.Entities;

namespace Identity.Api.Tests.Fixtures;

public class FakeUserRepository : IUserRepository
{
    private readonly Dictionary<Guid, (ApplicationUser User, string Password)> _store = new();

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken ct)
    {
        var exists = _store.Values.Any(x => x.User.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(exists);
    }

    public Task<ApplicationUser> CreateAsync(ApplicationUser user, string password, CancellationToken ct)
    {
        _store[user.Id] = (user, password);
        return Task.FromResult(user);
    }

    public Task<ApplicationUser?> FindByEmailAsync(string email, CancellationToken ct)
    {
        var match = _store.Values
            .FirstOrDefault(x => x.User.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<ApplicationUser?>(match.User);
    }

    public Task<ApplicationUser?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        _store.TryGetValue(id, out var entry);
        return Task.FromResult<ApplicationUser?>(entry.User);
    }

    public Task<bool> CheckPasswordAsync(ApplicationUser user, string password, CancellationToken ct)
    {
        _store.TryGetValue(user.Id, out var entry);
        return Task.FromResult(entry.Password == password);
    }

    public Task UpdateAsync(ApplicationUser user, CancellationToken ct)
    {
        if (_store.TryGetValue(user.Id, out var entry))
            _store[user.Id] = (user, entry.Password);
        return Task.CompletedTask;
    }

    public void Seed(ApplicationUser user, string password) => _store[user.Id] = (user, password);
}