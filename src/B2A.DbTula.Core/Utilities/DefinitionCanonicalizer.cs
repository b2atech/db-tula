using B2A.DbTula.Core.Models;
using System.Text.RegularExpressions;

namespace B2A.DbTula.Core.Utilities;

/// <summary>
/// Utility for canonicalizing database object definitions to enable ownership-agnostic comparison
/// </summary>
public static class DefinitionCanonicalizer
{
    /// <summary>
    /// Canonicalizes a DDL definition by removing owner/definer references and normalizing whitespace
    /// when comparison options specify to ignore ownership
    /// </summary>
    /// <param name="ddl">The DDL definition to canonicalize</param>
    /// <param name="dbKind">The database kind (postgres, mysql, etc.)</param>
    /// <param name="options">Comparison options</param>
    /// <returns>Canonicalized definition for comparison</returns>
    public static string CanonicalizeDefinition(string? ddl, string dbKind, ComparisonOptions options)
    {
        if (string.IsNullOrWhiteSpace(ddl))
            return string.Empty;

        var canonicalized = ddl;

        if (options.IgnoreOwnership)
        {
            canonicalized = RemoveOwnershipReferences(canonicalized, dbKind);
            canonicalized = RemoveDdlNoise(canonicalized, dbKind);
        }

        // Always normalize whitespace for consistent comparison
        canonicalized = NormalizeWhitespace(canonicalized);

        return canonicalized;
    }

    /// <summary>
    /// Removes ownership and definer references from DDL using explicit line-level patterns only.
    /// Does NOT use wildcard word.dot removal to avoid corrupting SQL bodies (type casts, table-qualified refs, etc.).
    /// </summary>
    private static string RemoveOwnershipReferences(string ddl, string dbKind)
    {
        var result = ddl;

        if (dbKind.Equals("postgres", StringComparison.OrdinalIgnoreCase))
        {
            // Remove full ALTER ... OWNER TO statements (safe: full statement on its own)
            result = Regex.Replace(result,
                @"ALTER\s+(TABLE|SEQUENCE|FUNCTION|PROCEDURE|VIEW|MATERIALIZED\s+VIEW)\s+[^;]+\s+OWNER\s+TO\s+\S+\s*;",
                "", RegexOptions.IgnoreCase | RegexOptions.Multiline);

            // Remove SET search_path lines only (full statement)
            result = Regex.Replace(result,
                @"^\s*SET\s+search_path\s*=\s*[^;]+;\s*$",
                "", RegexOptions.IgnoreCase | RegexOptions.Multiline);

            // Remove GRANT/REVOKE lines only (full statement)
            result = Regex.Replace(result,
                @"^\s*(GRANT|REVOKE)\s+[^;]+;\s*$",
                "", RegexOptions.IgnoreCase | RegexOptions.Multiline);

            // Strip 'public.' schema prefix ONLY when it appears directly after a DDL keyword,
            // never inside function bodies. This is safe because DDL keywords precede object names.
            result = Regex.Replace(result,
                @"(?<=\b(TABLE|VIEW|FUNCTION|PROCEDURE|SEQUENCE|INDEX|TRIGGER|ON)\s+)public\.",
                "", RegexOptions.IgnoreCase);
        }

        if (dbKind.Equals("mysql", StringComparison.OrdinalIgnoreCase))
        {
            // Remove DEFINER clauses in all common quoting styles
            result = Regex.Replace(result, @"DEFINER\s*=\s*`[^`]*`@`[^`]*`", "", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"DEFINER\s*=\s*'[^']*'@'[^']*'", "", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"DEFINER\s*=\s*\S+@\S+", "", RegexOptions.IgnoreCase);

            // Remove backtick-quoted database prefix: `dbname`. → (nothing)
            result = Regex.Replace(result, @"`[^`]+`\.", "", RegexOptions.IgnoreCase);
        }

        return result;
    }

    /// <summary>
    /// Removes DDL noise that doesn't affect functional comparison
    /// </summary>
    private static string RemoveDdlNoise(string ddl, string dbKind)
    {
        var result = ddl;

        // Common patterns for both databases
        
        // Remove comments
        result = Regex.Replace(result, @"--[^\r\n]*", "", RegexOptions.Multiline);
        result = Regex.Replace(result, @"/\*.*?\*/", "", RegexOptions.Singleline);
        
        // PostgreSQL specific noise
        if (dbKind.Equals("postgres", StringComparison.OrdinalIgnoreCase))
        {
            // Remove SET statements that don't affect structure
            result = Regex.Replace(result, @"SET\s+\w+\s*=\s*[^;]+;", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            
            // Remove security labels
            result = Regex.Replace(result, @"SECURITY\s+LABEL\s+[^;]+;", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            
            // Remove grants/revokes
            result = Regex.Replace(result, @"(GRANT|REVOKE)\s+[^;]+;", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        }

        // MySQL specific noise
        if (dbKind.Equals("mysql", StringComparison.OrdinalIgnoreCase))
        {
            // Remove SQL SECURITY clauses that are functionally equivalent
            result = Regex.Replace(result, @"SQL\s+SECURITY\s+(DEFINER|INVOKER)", "", RegexOptions.IgnoreCase);
            
            // Remove character set/collation that don't affect structure comparison
            // (Be careful - these might be functionally important, but for schema structure they're often noise)
        }

        return result;
    }

    /// <summary>
    /// Normalizes whitespace for consistent comparison
    /// </summary>
    private static string NormalizeWhitespace(string ddl)
    {
        if (string.IsNullOrWhiteSpace(ddl))
            return string.Empty;

        var result = ddl;
        
        // Replace multiple whitespace with single space
        result = Regex.Replace(result, @"\s+", " ", RegexOptions.Multiline);
        
        // Remove leading/trailing whitespace
        result = result.Trim();
        
        // Normalize line endings and remove empty lines
        result = Regex.Replace(result, @"^\s*$\n", "", RegexOptions.Multiline);
        
        return result;
    }
}