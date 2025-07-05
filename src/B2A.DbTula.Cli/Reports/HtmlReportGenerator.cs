using B2A.DbTula.Core.Models;
using RazorLight;
using System.Text.Json;

namespace B2A.DbTula.Cli.Reports;


public static class HtmlReportGenerator
{
    public static async Task GenerateWithRazorAsync(SchemaComparisonReport report, string outputPath)
    {
        var engine = new RazorLightEngineBuilder()
                         .UseFileSystemProject(Path.Combine(AppContext.BaseDirectory, "Reports", "Templates"))
                         .UseMemoryCachingProvider()
                         .Build();

        var result = await engine.CompileRenderAsync("ComparisonReport.cshtml", report);
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

