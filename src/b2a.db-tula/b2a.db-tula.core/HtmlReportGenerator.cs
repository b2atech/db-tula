using b2a.db_tula.core.Models;
using RazorLight;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace b2a.db_tula.core
{
    public static class HtmlReportGenerator
    {
        public static void Generate(List<ComparisonResult> comparisons, string filePath)
        {
            var sb = new StringBuilder();

            sb.AppendLine("<html>");
            sb.AppendLine("<head>");
            sb.AppendLine("<link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css' rel='stylesheet'>");
            sb.AppendLine("<script src='https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.5.0/highlight.min.js'></script>");
            sb.AppendLine("<link href='https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.5.0/styles/default.min.css' rel='stylesheet'>");
            sb.AppendLine("<script>hljs.highlightAll();</script>");
            sb.AppendLine("<style>");
            sb.AppendLine(".mismatch { background-color: #fff3cd; }");
            sb.AppendLine(".missing { background-color: #f8d7da; }");
            sb.AppendLine("details { white-space: pre-wrap; font-family: monospace; }");
            sb.AppendLine(".code-block { position: relative; }");
            sb.AppendLine(".copy-btn { position: absolute; top: 5px; right: 5px; z-index: 2; }");
            sb.AppendLine("</style>");
            sb.AppendLine("<script>");
            sb.AppendLine("function applyFilters() {");
            sb.AppendLine("  const mismatch = document.getElementById('mismatchOnly').checked;");
            sb.AppendLine("  const missingSource = document.getElementById('missingSourceOnly').checked;");
            sb.AppendLine("  const missingTarget = document.getElementById('missingTargetOnly').checked;");
            sb.AppendLine("  document.querySelectorAll('table tbody tr[data-comparison]').forEach(row => {");
            sb.AppendLine("    const comparison = row.dataset.comparison;");
            sb.AppendLine("    let show = true;");
            sb.AppendLine("    if (mismatch && comparison === 'Matching') show = false;");
            sb.AppendLine("    if (missingSource && comparison !== 'Missing in Source') show = false;");
            sb.AppendLine("    if (missingTarget && comparison !== 'Missing in Target') show = false;");
            sb.AppendLine("    row.style.display = show ? '' : 'none';");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine("function copyToClipboard(id) {");
            sb.AppendLine("  const el = document.getElementById(id);");
            sb.AppendLine("  navigator.clipboard.writeText(el.innerText).then(() => alert('Copied to clipboard'));");
            sb.AppendLine("}");
            sb.AppendLine("</script></head><body class='container my-4'>");

            sb.AppendLine("<h1 class='mb-4'>Schema Comparison Report</h1>");
            sb.AppendLine("<div class='mb-3'>");
            sb.AppendLine("<label class='form-check'><input class='form-check-input' type='checkbox' id='mismatchOnly' onchange='applyFilters()'> Show only mismatches</label>");
            sb.AppendLine("<label class='form-check'><input class='form-check-input' type='checkbox' id='missingSourceOnly' onchange='applyFilters()'> Show only missing in source</label>");
            sb.AppendLine("<label class='form-check'><input class='form-check-input' type='checkbox' id='missingTargetOnly' onchange='applyFilters()'> Show only missing in target</label>");
            sb.AppendLine("</div>");

            sb.AppendLine("<table class='table table-bordered table-striped'>");
            sb.AppendLine("<thead class='table-light'><tr><th>Type</th><th>Name</th><th>Source</th><th>Destination</th><th>Comparison</th><th>Definition</th></tr></thead>");
            sb.AppendLine("<tbody>");

            int idCounter = 0;
            foreach (var item in comparisons)
            {
                string sourceState = item.Comparison == "Missing in Source" ? "Missing" : "Present";
                string targetState = item.Comparison == "Missing in Target" ? "Missing" : "Present";

                if (item.Comparison == "Matching" || item.Comparison == "Not Matching")
                {
                    sourceState = "Present";
                    targetState = "Present";
                }

                string rowClass = item.Comparison switch
                {
                    "Matching" => "",
                    "Not Matching" => "mismatch",
                    "Missing in Target" or "Missing in Source" => "missing",
                    _ => ""
                };

                sb.AppendLine($"<tr class='{rowClass}' data-comparison='{item.Comparison}'>");
                sb.AppendLine($"<td>{item.Type}</td>");
                sb.AppendLine($"<td>{item.SourceName ?? item.DestinationName}</td>");
                sb.AppendLine($"<td>{sourceState}</td>");
                sb.AppendLine($"<td>{targetState}</td>");
                sb.AppendLine($"<td>{item.Comparison}</td>");

                if (!string.IsNullOrWhiteSpace(item.SourceDefinition) || !string.IsNullOrWhiteSpace(item.DestinationDefinition))
                {
                    string defId = $"code-block-{idCounter++}";
                    sb.AppendLine("<td><details><summary>View</summary>");
                    sb.AppendLine($"<div class='code-block'><button class='btn btn-sm btn-outline-secondary copy-btn' onclick=\"copyToClipboard('{defId}')\">Copy</button>");
                    sb.AppendLine($"<pre><code class='language-sql' id='{defId}'>");
                    if (!string.IsNullOrWhiteSpace(item.SourceDefinition))
                        sb.AppendLine(System.Net.WebUtility.HtmlEncode(item.SourceDefinition));
                    if (!string.IsNullOrWhiteSpace(item.DestinationDefinition))
                        sb.AppendLine(System.Net.WebUtility.HtmlEncode(item.DestinationDefinition));
                    sb.AppendLine("</code></pre></div></details></td>");
                }
                else
                {
                    sb.AppendLine("<td></td>");
                }

                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody></table>");
            sb.AppendLine("</body></html>");
            File.WriteAllText(filePath, sb.ToString());
        }

        public static async Task GenerateWithRazorAsync(List<ComparisonResult> comparisons, string outputPath)
        {
            var engine = new RazorLightEngineBuilder()
                             .UseFileSystemProject(Path.Combine(AppContext.BaseDirectory, "Templates"))
                             .UseMemoryCachingProvider()
                                    .Build();


            var result = await engine.CompileRenderAsync("SchemaReport.cshtml", comparisons);

            File.WriteAllText(outputPath, result);
        }

        public static void SaveAsJson(SchemaComparisonReport report, string filePath)
        {
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }

        public static SchemaComparisonReport LoadFromJson(string filePath)
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<SchemaComparisonReport>(json);
        }
    }
}
