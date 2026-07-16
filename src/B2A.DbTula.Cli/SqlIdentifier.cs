namespace B2A.DbTula.Cli;

/// <summary>
/// Quotes object names for generated SQL. Table (and other top-level object) names carry an
/// explicit schema prefix as "schema.object" — naively wrapping that whole string in one pair of
/// quotes (`"schema.object"`) would create a single identifier containing a literal dot instead of
/// a schema-qualified reference. This splits on the first '.' and quotes each part separately.
/// </summary>
internal static class SqlIdentifier
{
    public static string Quote(string qualifiedName)
    {
        var idx = qualifiedName.IndexOf('.');
        return idx < 0
            ? $"\"{qualifiedName}\""
            : $"\"{qualifiedName[..idx]}\".\"{qualifiedName[(idx + 1)..]}\"";
    }
}
