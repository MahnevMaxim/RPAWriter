using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    /// <summary>
    /// Детерминированная заглушка AI-провайдера (офлайн/демо-режим).
    /// Разбивает текст по предложениям и подбирает тип активности по ключевым словам.
    /// </summary>
    public class StubAiProvider : IAiProvider
    {
        private static readonly Dictionary<string, string> ActivityMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "открыть", "OpenBrowser" },
                { "кликнуть", "ClickElement" },
                { "нажать", "ClickElement" },
                { "прочитать", "ReadTable" },
                { "заполнить", "FillField" },
                { "отправить", "Submit" },
                { "записать", "WriteExcel" },
                { "сохранить", "WriteExcel" },
                { "open", "OpenBrowser" },
                { "click", "ClickElement" },
                { "read", "ReadTable" },
                { "write", "WriteExcel" },
                { "send", "Submit" }
            };

        public async Task<SpecAnalysis> AnalyzeSpecAsync(SpecModel spec, CancellationToken cancellationToken = default)
        {
            List<TaskDescriptor> tasks;
            if (spec?.Tasks != null && spec.Tasks.Count > 0)
                tasks = spec.Tasks.Select(CloneTask).ToList();
            else
                tasks = (await DecomposeTasksAsync(spec?.RawText ?? "", cancellationToken)).Select(CloneTask).ToList();

            return new SpecAnalysis
            {
                Summary = "Заглушка: анализ ТЗ \"" + (spec?.Name ?? "") + "\"",
                Tasks = tasks
            };
        }

        public Task<RobotPlan> GenerateRobotAsync(SpecAnalysis analysis, CancellationToken cancellationToken = default)
        {
            var plan = new RobotPlan { Name = "Robot" };
            var root = new ActivityNode { Kind = ActivityNodeKind.Sequence, Name = "Main" };
            root.Children.Add(new ActivityNode { Kind = ActivityNodeKind.WriteLine, Expression = "Запуск робота" });
            foreach (var task in analysis?.Tasks ?? new List<TaskDescriptor>())
            {
                root.Children.Add(new ActivityNode
                {
                    Kind = ActivityNodeKind.InvokeMethod,
                    Name = task.Description ?? task.Action,
                    ClassName = "RpaActions",
                    MethodName = MethodNameFor(task),
                    Arguments = new List<ArgumentAssignment>
                    {
                        new ArgumentAssignment { Parameter = "target", Value = task.Target }
                    }
                });
            }
            plan.Root = root;
            return Task.FromResult(plan);
        }

        public Task<IReadOnlyList<TaskDescriptor>> DecomposeTasksAsync(string text, CancellationToken cancellationToken = default)
        {
            var tasks = new List<TaskDescriptor>();
            if (string.IsNullOrWhiteSpace(text)) return Task.FromResult<IReadOnlyList<TaskDescriptor>>(tasks);

            foreach (var sentence in Regex.Split(text, @"(?<=[.!?;])\s+"))
            {
                if (string.IsNullOrWhiteSpace(sentence)) continue;
                var action = GuessAction(sentence);
                tasks.Add(new TaskDescriptor
                {
                    Action = action,
                    Target = sentence.Trim(),
                    Description = sentence.Trim(),
                    ActivityType = ActivityMap.ContainsKey(action) ? ActivityMap[action] : "Custom",
                    Keywords = TextNormalizer.Tokenize(sentence).ToList()
                });
            }
            return Task.FromResult<IReadOnlyList<TaskDescriptor>>(tasks);
        }

        public Task<SolutionRecord> MapTaskToSolutionAsync(TaskDescriptor task, string robotCode, CancellationToken cancellationToken = default)
        {
            var method = MethodNameFor(task);
            var record = new SolutionRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = task?.Description ?? task?.Action ?? "Task",
                Tasks = new List<TaskDescriptor> { CloneTask(task) },
                XamlFragment = "<InvokeMethod MethodName=\"" + method + "\" TargetType=\"Robots.RpaActions\" />",
                CSharpCode = "public static void " + method + "() { /* TODO */ }"
            };
            return Task.FromResult(record);
        }

        private static string MethodNameFor(TaskDescriptor task)
        {
            var action = task?.Action ?? "Run";
            if (ActivityMap.TryGetValue(action, out var mapped)) return mapped;
            var id = new string(action.Where(char.IsLetterOrDigit).ToArray());
            return string.IsNullOrWhiteSpace(id) ? "Run" : id;
        }

        private static string GuessAction(string text)
        {
            foreach (var kv in ActivityMap)
                if (text.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return kv.Key;
            return "выполнить";
        }

        private static TaskDescriptor CloneTask(TaskDescriptor source)
        {
            if (source == null) return null;
            var t = new TaskDescriptor
            {
                Action = source.Action,
                Target = source.Target,
                Description = source.Description,
                ActivityType = source.ActivityType
            };
            if (source.Inputs != null) t.Inputs.AddRange(source.Inputs);
            if (source.Outputs != null) t.Outputs.AddRange(source.Outputs);
            if (source.Keywords != null) t.Keywords.AddRange(source.Keywords);
            return t;
        }
    }
}
