namespace B2A.DbTula.Infrastructure.Postgres;

internal readonly record struct PostgresObjectName(string Schema, string Name)
{
    public string FullName => $"{Schema}.{Name}";

    public static PostgresObjectName Parse(string value, string defaultSchema = "public")
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Object name cannot be empty.", nameof(value));

        var clean = value.Trim();

        var parts = clean.Split(
            '.',
            2,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 2)
        {
            return new PostgresObjectName(
                NormalizeIdentifier(parts[0]),
                NormalizeIdentifier(parts[1]));
        }

        return new PostgresObjectName(defaultSchema, NormalizeIdentifier(clean));
    }

    public static PostgresObjectName ParseRoutine(string value, string defaultSchema = "public")
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Routine name cannot be empty.", nameof(value));

        var nameWithoutArguments = value.Split('(', 2)[0];

        return Parse(nameWithoutArguments, defaultSchema);
    }

    private static string NormalizeIdentifier(string value)
    {
        return value.Trim().Trim('"');
    }
}