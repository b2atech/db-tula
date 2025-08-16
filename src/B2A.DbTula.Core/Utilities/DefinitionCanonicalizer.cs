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
    /// Removes ownership and definer references from DDL
    /// </summary>
    private static string RemoveOwnershipReferences(string ddl, string dbKind)
    {
        var result = ddl;

        // PostgreSQL specific patterns
        if (dbKind.Equals("postgres", StringComparison.OrdinalIgnoreCase))
        {
            // Remove OWNER TO clauses: ALTER ... OWNER TO username;
            result = Regex.Replace(result, @"ALTER\s+[^\s]+\s+[^\s]+\s+OWNER\s+TO\s+[^;]+;", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            
            // Remove owner prefixes from object names: owner.object_name -> object_name  
            result = Regex.Replace(result, @"\bpublic\.", "", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\b\w+\.", "", RegexOptions.IgnoreCase);
            
            // Remove SET search_path statements
            result = Regex.Replace(result, @"SET\s+search_path\s*=\s*[^;]+;", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            
            // Remove schema qualifications in CREATE statements (when not needed for comparison)
            result = Regex.Replace(result, @"CREATE\s+(\w+)\s+\w+\.", "CREATE $1 ", RegexOptions.IgnoreCase);
        }

        // MySQL specific patterns  
        if (dbKind.Equals("mysql", StringComparison.OrdinalIgnoreCase))
        {
            // Remove DEFINER clauses: DEFINER=`username`@`host`
            result = Regex.Replace(result, @"DEFINER\s*=\s*`[^`]+`@`[^`]+`", "", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"DEFINER\s*=\s*'[^']+'@'[^']+'", "", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"DEFINER\s*=\s*[^\s]+@[^\s]+", "", RegexOptions.IgnoreCase);
            
            // Remove database name prefixes: database.object_name -> object_name
            result = Regex.Replace(result, @"`[^`]+`\.", "", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\b\w+\.", "", RegexOptions.IgnoreCase);
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