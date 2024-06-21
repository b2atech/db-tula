using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace b2a.db_tula
{
    public static class ConnectionManager
    {
        private static readonly string AppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "db_tula");
        private static readonly string ConnectionsFile = Path.Combine(AppDataFolder, "connections.json");

        static ConnectionManager()
        {
            if (!Directory.Exists(AppDataFolder))
            {
                Directory.CreateDirectory(AppDataFolder);
            }
        }

        public static List<SavedConnection> LoadConnections()
        {
            if (File.Exists(ConnectionsFile))
            {
                string json = File.ReadAllText(ConnectionsFile);
                return JsonSerializer.Deserialize<List<SavedConnection>>(json);
            }
            return new List<SavedConnection>();
        }

        public static void SaveConnections(List<SavedConnection> connections)
        {
            string json = JsonSerializer.Serialize(connections);
            File.WriteAllText(ConnectionsFile, json);
        }
    }
}
