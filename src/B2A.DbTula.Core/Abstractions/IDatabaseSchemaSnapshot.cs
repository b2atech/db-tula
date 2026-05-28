using B2A.DbTula.Core.Models;

namespace B2A.DbTula.Core.Abstractions;

/// <summary>
/// Fetches the complete schema metadata for one database in a small number of bulk queries.
/// Providers that implement this interface replace the per-table N+1 query pattern.
/// </summary>
public interface IDatabaseSchemaSnapshot
{
    Task<SchemaSnapshot> TakeSnapshotAsync(CancellationToken ct = default);
}
