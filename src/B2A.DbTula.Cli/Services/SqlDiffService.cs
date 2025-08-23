using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace B2A.DbTula.Cli.Services;

/// <summary>
/// Service for computing detailed side-by-side SQL script differences using DiffPlex
/// </summary>
public class SqlDiffService
{
    private readonly IDiffer _differ;
    private readonly ISideBySideDiffBuilder _diffBuilder;

    public SqlDiffService()
    {
        _differ = new Differ();
        _diffBuilder = new SideBySideDiffBuilder(_differ);
    }

    /// <summary>
    /// Computes a side-by-side diff between source and target SQL scripts
    /// </summary>
    /// <param name="sourceScript">Source SQL script</param>
    /// <param name="targetScript">Target SQL script</param>
    /// <returns>Detailed diff information for rendering</returns>
    public SqlDiffResult ComputeDiff(string? sourceScript, string? targetScript)
    {
        var cleanSourceScript = CleanScript(sourceScript);
        var cleanTargetScript = CleanScript(targetScript);

        var diffResult = _diffBuilder.BuildDiffModel(cleanSourceScript, cleanTargetScript);
        
        return new SqlDiffResult
        {
            SourceScript = cleanSourceScript,
            TargetScript = cleanTargetScript,
            OldText = diffResult.OldText,
            NewText = diffResult.NewText,
            HasDifferences = diffResult.OldText.Lines.Any(l => l.Type != ChangeType.Unchanged) ||
                           diffResult.NewText.Lines.Any(l => l.Type != ChangeType.Unchanged)
        };
    }

    /// <summary>
    /// Generates HTML for side-by-side diff view
    /// </summary>
    /// <param name="diffResult">Computed diff result</param>
    /// <returns>HTML string for side-by-side diff visualization</returns>
    public string GenerateSideBySideHtml(SqlDiffResult diffResult)
    {
        if (!diffResult.HasDifferences)
        {
            return $@"
                <div class='sql-diff-container no-differences'>
                    <div class='sql-diff-header'>
                        <span class='no-diff-message'>No differences detected</span>
                    </div>
                    <div class='sql-diff-content'>
                        <pre class='sql-script'><code class='language-sql'>{EscapeHtml(diffResult.SourceScript)}</code></pre>
                    </div>
                </div>";
        }

        var leftLines = diffResult.OldText.Lines;
        var rightLines = diffResult.NewText.Lines;
        var maxLines = Math.Max(leftLines.Count, rightLines.Count);
        
        var html = @"
            <div class='sql-diff-container'>
                <div class='sql-diff-header'>
                    <div class='sql-diff-pane-header source-header'>
                        <i class='bi bi-file-text'></i> Source Script
                    </div>
                    <div class='sql-diff-pane-header target-header'>
                        <i class='bi bi-file-text'></i> Target Script
                    </div>
                </div>
                <div class='sql-diff-content'>";

        for (int i = 0; i < maxLines; i++)
        {
            var leftLine = i < leftLines.Count ? leftLines[i] : null;
            var rightLine = i < rightLines.Count ? rightLines[i] : null;
            
            var leftClass = GetCssClass(leftLine?.Type);
            var rightClass = GetCssClass(rightLine?.Type);
            
            var leftText = leftLine?.Text ?? "";
            var rightText = rightLine?.Text ?? "";
            
            var leftLineNumber = leftLine?.Position ?? 0;
            var rightLineNumber = rightLine?.Position ?? 0;

            html += $@"
                <div class='sql-diff-row {(leftClass == rightClass ? leftClass : "diff-mixed")}'>
                    <div class='sql-diff-pane sql-diff-left {leftClass}'>
                        <span class='line-number'>{(leftLineNumber > 0 ? leftLineNumber.ToString() : "")}</span>
                        <span class='line-content'><code class='language-sql'>{EscapeHtml(leftText)}</code></span>
                    </div>
                    <div class='sql-diff-pane sql-diff-right {rightClass}'>
                        <span class='line-number'>{(rightLineNumber > 0 ? rightLineNumber.ToString() : "")}</span>
                        <span class='line-content'><code class='language-sql'>{EscapeHtml(rightText)}</code></span>
                    </div>
                </div>";
        }

        html += @"
                </div>
            </div>";

        return html;
    }

    private static string CleanScript(string? script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return "";
            
        return script.Trim();
    }

    private static string GetCssClass(ChangeType? changeType)
    {
        return changeType switch
        {
            ChangeType.Deleted => "diff-deleted",
            ChangeType.Inserted => "diff-inserted", 
            ChangeType.Modified => "diff-modified",
            ChangeType.Unchanged => "diff-unchanged",
            _ => ""
        };
    }

    private static string EscapeHtml(string text)
    {
        return System.Net.WebUtility.HtmlEncode(text);
    }
}

/// <summary>
/// Result of SQL diff computation
/// </summary>
public class SqlDiffResult
{
    public string SourceScript { get; set; } = "";
    public string TargetScript { get; set; } = "";
    public DiffPaneModel OldText { get; set; } = new();
    public DiffPaneModel NewText { get; set; } = new();
    public bool HasDifferences { get; set; }
}