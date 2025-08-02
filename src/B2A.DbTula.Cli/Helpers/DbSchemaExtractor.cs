using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using B2a.DbTula.Core.Abstractions;

namespace B2A.DbTula.Cli.Helpers
{
    public class ExtractedDbObject
    {
        public string Name { get; set; }
        public string Type { get; set; } // Function, Procedure, View, Trigger
        public string Sql { get; set; }
    }

    public class DbSchemaExtractor
    {
        private readonly IDatabaseSchemaProvider _provider;

        public DbSchemaExtractor(IDatabaseSchemaProvider provider)
        {
            _provider = provider;
        }

        /// <summary>
        /// Extracts all supported object types, with progress logging.
        /// </summary>
        public async Task<List<ExtractedDbObject>> ExtractAllAsync(
     IEnumerable<string>? objectTypes = null,
     int? limit = null,
     Action<string>? progressLogger = null)
        {
            var extracted = new List<ExtractedDbObject>();
            objectTypes ??= new[] { "functions", "procedures", "views", "triggers" };

            progressLogger?.Invoke("🔍 Starting extraction...");

            if (objectTypes.Contains("functions"))
            {
                progressLogger?.Invoke("⚙️ Extracting functions...");
                var functions = await _provider.GetFunctionsAsync();
                if (limit.HasValue) functions = functions.Take(limit.Value).ToList();

                foreach (var fn in functions)
                {
                    var sql = await _provider.GetFunctionDefinitionAsync(fn.Name);
                    extracted.Add(new ExtractedDbObject
                    {
                        Name = fn.Name,
                        Type = "Function",
                        Sql = sql
                    });
                    progressLogger?.Invoke($"   📝 Extracted function: {fn.Name}");
                }
                progressLogger?.Invoke($"✅ Extracted {functions.Count} functions.");
            }

            if (objectTypes.Contains("procedures"))
            {
                progressLogger?.Invoke("🛠 Extracting procedures...");
                var procs = await _provider.GetProceduresAsync();
                if (limit.HasValue) procs = procs.Take(limit.Value).ToList();

                foreach (var proc in procs)
                {
                    var sql = await _provider.GetProcedureDefinitionAsync(proc.Name);
                    extracted.Add(new ExtractedDbObject
                    {
                        Name = proc.Name,
                        Type = "Procedure",
                        Sql = sql
                    });
                    progressLogger?.Invoke($"   📝 Extracted procedure: {proc.Name}");
                }
                progressLogger?.Invoke($"✅ Extracted {procs.Count} procedures.");
            }

            // Uncomment and implement if your provider supports views/triggers
            /*
            if (objectTypes.Contains("views"))
            {
                progressLogger?.Invoke("👁 Extracting views...");
                var views = await _provider.GetViewsAsync();
                if (limit.HasValue) views = views.Take(limit.Value).ToList();

                foreach (var view in views)
                {
                    var sql = await _provider.GetViewDefinitionAsync(view.Name);
                    extracted.Add(new ExtractedDbObject
                    {
                        Name = view.Name,
                        Type = "View",
                        Sql = sql
                    });
                    progressLogger?.Invoke($"   📝 Extracted view: {view.Name}");
                }
                progressLogger?.Invoke($"✅ Extracted {views.Count} views.");
            }

            if (objectTypes.Contains("triggers"))
            {
                progressLogger?.Invoke("🔔 Extracting triggers...");
                var triggers = await _provider.GetTriggersAsync();
                if (limit.HasValue) triggers = triggers.Take(limit.Value).ToList();

                foreach (var trigger in triggers)
                {
                    var sql = await _provider.GetTriggerDefinitionAsync(trigger.Name);
                    extracted.Add(new ExtractedDbObject
                    {
                        Name = trigger.Name,
                        Type = "Trigger",
                        Sql = sql
                    });
                    progressLogger?.Invoke($"   📝 Extracted trigger: {trigger.Name}");
                }
                progressLogger?.Invoke($"✅ Extracted {triggers.Count} triggers.");
            }
            */

            progressLogger?.Invoke("✅ Extraction completed.");
            return extracted;
        }

        /// <summary>
        /// Writes objects to a cleaned output directory, using folder logic for duplicates.
        /// </summary>
        public static void WriteToDirectory(List<ExtractedDbObject> objects, string outputDir, Action<string>? progressLogger = null)
        {
            progressLogger?.Invoke($"🧹 Cleaning output directory: {outputDir}");
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, recursive: true);
            }

            var objectsByType = objects.GroupBy(o => o.Type);
            foreach (var typeGroup in objectsByType)
            {
                var typeDir = Path.Combine(outputDir, typeGroup.Key);
                Directory.CreateDirectory(typeDir);

                var nameGroups = typeGroup.GroupBy(o => o.Name);

                foreach (var nameGroup in nameGroups)
                {
                    var count = nameGroup.Count();
                    if (count == 1)
                    {
                        // Only one object with this name: write as Type/name.sql
                        var obj = nameGroup.First();
                        var fileName = Path.Combine(typeDir, $"{obj.Name}.sql");
                        File.WriteAllText(fileName, obj.Sql ?? string.Empty);
                        progressLogger?.Invoke($"📝 Wrote {fileName}");
                    }
                    else
                    {
                        // Multiple: create Type/name/definition.sql and Type/name/hash.sql for each
                        var nameDir = Path.Combine(typeDir, nameGroup.Key);
                        Directory.CreateDirectory(nameDir);

                        int index = 1;
                        foreach (var obj in nameGroup)
                        {
                            string fileName;
                            if (index == 1)
                            {
                                // First is always definition.sql
                                fileName = Path.Combine(nameDir, "definition.sql");
                            }
                            else
                            {
                                // Others use a hash of their SQL as filename
                                var hash = obj.Sql != null ? obj.Sql.GetHashCode().ToString("x") : $"alt_{index}.sql";
                                fileName = Path.Combine(nameDir, $"{hash}.sql");
                            }
                            File.WriteAllText(fileName, obj.Sql ?? string.Empty);
                            progressLogger?.Invoke($"📝 Wrote {fileName}");
                            index++;
                        }
                    }
                }
            }
            progressLogger?.Invoke("✅ All files written.");
        }
    }
}