using System.Collections.Generic;
using System.Linq;
using System.Text;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>Вспомогательные построители Markdown (таблицы, список задач).</summary>
    public static class MarkdownContent
    {
        /// <summary>Экранирует значение ячейки таблицы для Markdown.</summary>
        public static string EscapeCell(string value)
        {
            if (value == null) return "";
            return value
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("|", "\\|");
        }

        /// <summary>Строит Markdown-таблицу; первая строка считается заголовком.</summary>
        public static string Table(IEnumerable<IReadOnlyList<string>> rows)
        {
            var list = rows?.ToList() ?? new List<IReadOnlyList<string>>();
            if (list.Count == 0) return "";
            int columnCount = list.Max(r => r?.Count ?? 0);
            if (columnCount == 0) return "";

            var sb = new StringBuilder();
            for (int r = 0; r < list.Count; r++)
            {
                var row = list[r];
                sb.Append("| ");
                for (int c = 0; c < columnCount; c++)
                {
                    var cell = row != null && c < row.Count ? row[c] : "";
                    sb.Append(EscapeCell(cell));
                    if (c < columnCount - 1) sb.Append(" | ");
                }
                sb.AppendLine(" |");
                if (r == 0)
                {
                    sb.Append("|");
                    for (int c = 0; c < columnCount; c++) sb.Append(" --- |");
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        /// <summary>Строит содержимое tasks.md из списка задач ТЗ.</summary>
        public static string TasksToMarkdown(SpecModel spec)
        {
            var name = spec?.Name ?? "ТЗ";
            var sb = new StringBuilder();
            sb.AppendLine("# Задачи — " + name);
            sb.AppendLine();

            var rows = new List<IReadOnlyList<string>>
            {
                new List<string> { "№", "Действие", "Объект", "Тип активности", "Ключевые слова" }
            };

            var tasks = spec?.Tasks ?? new List<TaskDescriptor>();
            for (int i = 0; i < tasks.Count; i++)
            {
                var t = tasks[i];
                rows.Add(new List<string>
                {
                    (i + 1).ToString(),
                    t?.Action ?? "",
                    t?.Target ?? "",
                    t?.ActivityType ?? "",
                    string.Join(", ", t?.Keywords ?? new List<string>())
                });
            }

            sb.Append(Table(rows));
            return sb.ToString();
        }
    }
}
