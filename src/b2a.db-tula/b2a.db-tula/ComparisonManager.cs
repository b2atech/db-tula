using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace b2a.db_tula
{
    public static class ComparisonManager
    {
        private static readonly string AppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "db_tula");
        private static readonly string ComparisonsFile = Path.Combine(AppDataFolder, "comparisons.json");

        static ComparisonManager()
        {
            if (!Directory.Exists(AppDataFolder))
            {
                Directory.CreateDirectory(AppDataFolder);
            }
        }

        public static List<SavedComparison> LoadComparisons()
        {
            if (File.Exists(ComparisonsFile))
            {
                string json = File.ReadAllText(ComparisonsFile);
                return JsonSerializer.Deserialize<List<SavedComparison>>(json);
            }
            return new List<SavedComparison>();
        }

        public static void SaveComparisons(List<SavedComparison> comparisons)
        {
            string json = JsonSerializer.Serialize(comparisons);
            File.WriteAllText(ComparisonsFile, json);
        }
    }
}
