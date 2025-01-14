using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ProxmoxApiHelper.Helpers
{
    public class SettingsManager
    {
        private static readonly string SettingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private static Settings _currentSettings;

        public static Settings CurrentSettings
        {
            get
            {
                if (_currentSettings == null)
                {
                    _currentSettings = LoadSettings();
                }
                return _currentSettings;
            }
        }

        private static Settings LoadSettings()
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
            }
            return new Settings();
        }

        public static async Task SaveSettingsAsync()
        {
            string json = JsonSerializer.Serialize(CurrentSettings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(SettingsFilePath, json);
        }
    }

    public class Settings
    {
        public int RefreshInterval { get; set; } = 5;
        public string Theme { get; set; } = "System";
    }
}
