namespace B2A.DbTula.Core.Enums;

/// <summary>
/// Risk severity level for a schema change, matching Atlas lint rule categories.
/// </summary>
public enum LintSeverity
{
    None,
    Info,
    Warning,
    Error
}
