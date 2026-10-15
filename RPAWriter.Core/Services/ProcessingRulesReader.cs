using System;
using System.IO;
using System.Linq;
using System.Text;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Читает markdown-файлы с правилами обработки ТЗ и формирования задач из каталога.
    /// </summary>
    public static class ProcessingRulesReader
    {
        /// <summary>
        /// Возвращает объединённый текст всех *.md / *.markdown файлов из каталога
        /// (без подпапок), отсортированных по имени. Пустой каталог/путь — пустая строка.
        /// </summary>
        public static string ReadAll(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return "";
            if (!Directory.Exists(folder)) return "";

            var files = Directory.GetFiles(folder, "*.md", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(folder, "*.markdown", SearchOption.TopDirectoryOnly))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0) return "";

            var sb = new StringBuilder();
            foreach (var file in files)
            {
                string text;
                try
                {
                    text = File.ReadAllText(file, Encoding.UTF8);
                }
                catch
                {
                    continue; // недоступный файл пропускаем
                }

                if (string.IsNullOrWhiteSpace(text)) continue;

                sb.AppendLine("## Правила: " + Path.GetFileName(file));
                sb.AppendLine(text);
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
