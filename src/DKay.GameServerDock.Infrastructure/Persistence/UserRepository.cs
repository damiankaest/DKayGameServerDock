using DKay.GameServerDock.Application.Abstractions;
using DKay.GameServerDock.Domain;
using Microsoft.EntityFrameworkCore;

namespace DKay.GameServerDock.Infrastructure.Persistence;

public sealed class UserRepository(AppDbContext database) : IUserRepository
{
    public Task<bool> AnyAsync(CancellationToken cancellationToken) => database.Users.AnyAsync(cancellationToken);

    public Task<LocalUser?> FindByNameAsync(string userName, CancellationToken cancellationToken) =>
        database.Users.SingleOrDefaultAsync(user => user.UserName == userName, cancellationToken);

    public async Task AddAsync(LocalUser user, CancellationToken cancellationToken)
    {
        database.Users.Add(user);
        await database.SaveChangesAsync(cancellationToken);
    }
}

