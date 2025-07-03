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
