namespace B2A.DbTula.Core.Models;

/// <summary>
/// Complete metadata snapshot of one database, fetched in bulk.
/// All collections are keyed by table/object name (case-insensitive).
/// </summary>
public class SchemaSnapshot
{
    public IReadOnlyList<string> TableNames { get; init; } = [];

    public IReadOnlyDictionary<string, List<ColumnDefinition>> ColumnsByTable { get; init; }
        = new Dictionary<string, List<ColumnDefinition>>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, List<PrimaryKeyDefinition>> PrimaryKeysByTable { get; init; }
        = new Dictionary<string, List<PrimaryKeyDefinition>>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, List<ForeignKeyDefinition>> ForeignKeysByTable { get; init; }
        = new Dictionary<string, List<ForeignKeyDefinition>>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, List<IndexDefinition>> IndexesByTable { get; init; }
        = new Dictionary<string, List<IndexDefinition>>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, List<UniqueConstraintDefinition>> UniqueConstraintsByTable { get; init; }
        = new Dictionary<string, List<UniqueConstraintDefinition>>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, List<CheckConstraintDefinition>> CheckConstraintsByTable { get; init; }
        = new Dictionary<string, List<CheckConstraintDefinition>>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<DbFunctionDefinition> Functions { get; init; } = [];
    public IReadOnlyList<DbFunctionDefinition> Procedures { get; init; } = [];
    public IReadOnlyList<DbViewDefinition> Views { get; init; } = [];
    public IReadOnlyList<DbTriggerDefinition> Triggers { get; init; } = [];
    public IReadOnlyList<DbSequenceDefinition> Sequences { get; init; } = [];

    public HashSet<string> MaterializedViewNames { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Builds a TableDefinition for a given table name from the snapshot's dictionaries.
    /// </summary>
    public TableDefinition GetTableDefinition(string tableName) => new()
    {
        Name = tableName,
        Columns           = ColumnsByTable.GetValueOrDefault(tableName) ?? [],
        PrimaryKeys       = PrimaryKeysByTable.GetValueOrDefault(tableName) ?? [],
        ForeignKeys       = ForeignKeysByTable.GetValueOrDefault(tableName) ?? [],
        Indexes           = IndexesByTable.GetValueOrDefault(tableName) ?? [],
        UniqueConstraints = UniqueConstraintsByTable.GetValueOrDefault(tableName) ?? [],
    };
}
