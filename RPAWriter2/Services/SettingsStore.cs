using System;
using System.IO;
using Newtonsoft.Json;

namespace RPAWriter2.Services
{
    /// <summary>Настройки расширения, сохраняемые локально в JSON.</summary>
    public class SettingsData
    {
        public string BaseUrl { get; set; } = "https://api.openai.com/v1";
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "gpt-4o-mini";
        public string KnowledgeBasePath { get; set; } = "";
        public string RobotsRootPath { get; set; } = "";
        public string RobotTemplateName { get; set; } = "";
    }

    /// <summary>Загрузка/сохранение настроек в %AppData%\RPAWriter2\settings.json.</summary>
    public static class SettingsStore
    {
        public static string SettingsDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RPAWriter2");

        public static string SettingsFile => Path.Combine(SettingsDirectory, "settings.json");

        public static SettingsData Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    return JsonConvert.DeserializeObject<SettingsData>(json) ?? new SettingsData();
                }
            }
            catch
            {
                // игнорируем ошибки чтения — вернём значения по умолчанию
            }
            return new SettingsData();
        }

        public static void Save(SettingsData settings)
        {
            if (settings == null) return;
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(settings, Formatting.Indented));
            }
            catch
            {
                // игнорируем ошибки записи
            }
        }
    }
}
