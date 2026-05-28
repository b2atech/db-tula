using B2A.DbTula.Core.Models;

namespace B2a.DbTula.Core.Abstractions;

public interface IDatabaseIdentityProvider
{
    Task<DatabaseIdentity> GetDatabaseIdentityAsync(CancellationToken cancellationToken = default);
}