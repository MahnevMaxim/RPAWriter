using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Core.Services
{
    public class OpenAiOptions
    {
        public string BaseUrl { get; set; } = "https://api.openai.com/v1";
        public string ApiKey { get; set; }
        public string Model { get; set; } = "gpt-4o-mini";
        public double Temperature { get; set; } = 0.2;
        public string SystemPrompt { get; set; } =
            "Ты агент разработки RPA-роботов на Windows Workflow Foundation. " +
            "Отвечай строго валидным JSON без пояснений.";

        /// <summary>Промпт для свободного чата (обычный текст, без требования JSON).</summary>
        public string ChatSystemPrompt { get; set; } =
            "Ты — ассистент-разработчик RPA-роботов на Windows Workflow Foundation. " +
            "Отвечай кратко и по делу обычным текстом, без JSON, если явно не попросят.";
    }

    /// <summary>
    /// OpenAI-совместимый провайдер: POST {BaseUrl}/chat/completions.
    /// Работает с OpenAI, Azure OpenAI (совместимый endpoint), Ollama, LM Studio, vLLM.
    /// </summary>
    public class OpenAiCompatibleProvider : IAiProvider
    {
        private readonly HttpClient _http;
        private readonly OpenAiOptions _options;

        public OpenAiCompatibleProvider(OpenAiOptions options) : this(options, new HttpClient()) { }

        public OpenAiCompatibleProvider(OpenAiOptions options, HttpClient http)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        public async Task<SpecAnalysis> AnalyzeSpecAsync(SpecModel spec, CancellationToken cancellationToken = default)
        {
            var content = await CompleteAsync(
                "Проанализируй техническое задание и верни JSON вида " +
                "{\"summary\":\"...\",\"tasks\":[{\"action\":\"...\",\"target\":\"...\",\"description\":\"...\",\"activityType\":\"...\"}]}.\n\nТЗ:\n" +
                (spec?.RawText ?? ""), cancellationToken).ConfigureAwait(false);

            var obj = ParseJson(content);
            var analysis = new SpecAnalysis { Summary = obj.Value<string>("summary") ?? "" };
            foreach (var t in obj["tasks"] ?? new JArray())
            {
                analysis.Tasks.Add(new TaskDescriptor
                {
                    Action = t.Value<string>("action"),
                    Target = t.Value<string>("target"),
                    Description = t.Value<string>("description"),
                    ActivityType = t.Value<string>("activityType")
                });
            }
            return analysis;
        }

        public async Task<RobotPlan> GenerateRobotAsync(SpecAnalysis analysis, CancellationToken cancellationToken = default)
        {
            var payload = JsonConvert.SerializeObject(analysis);
            var content = await CompleteAsync(
                "Построй план workflow. Верни JSON вида {\"name\":\"...\",\"steps\":[" +
                "{\"kind\":\"InvokeMethod\",\"className\":\"RpaActions\",\"methodName\":\"...\"}," +
                "{\"kind\":\"Assign\",\"variable\":\"...\",\"expression\":\"...\"}]}. " +
                "kind: Sequence|InvokeMethod|Assign|WriteLine|Delay. Исходный анализ: " + payload,
                cancellationToken).ConfigureAwait(false);

            var obj = ParseJson(content);
            var plan = new RobotPlan { Name = obj.Value<string>("name") ?? "Workflow" };
            var root = new ActivityNode { Kind = ActivityNodeKind.Sequence, Name = "Root" };
            foreach (var step in obj["steps"] ?? new JArray())
                root.Children.Add(ParseStep(step));
            plan.Root = root;
            return plan;
        }

        public async Task<IReadOnlyList<TaskDescriptor>> DecomposeTasksAsync(string text, CancellationToken cancellationToken = default)
        {
            var content = await CompleteAsync(
                "Разбей документ на мелкие атомарные задачи. Верни JSON-массив: " +
                "[{\"action\":\"...\",\"target\":\"...\",\"description\":\"...\",\"activityType\":\"...\"}].\n\nДокумент:\n" +
                (text ?? ""), cancellationToken).ConfigureAwait(false);

            var array = ParseArray(content);
            var result = new List<TaskDescriptor>();
            if (array != null)
            {
                foreach (var t in array)
                {
                    result.Add(new TaskDescriptor
                    {
                        Action = t.Value<string>("action"),
                        Target = t.Value<string>("target"),
                        Description = t.Value<string>("description"),
                        ActivityType = t.Value<string>("activityType")
                    });
                }
            }
            return result;
        }

        public async Task<SolutionRecord> MapTaskToSolutionAsync(TaskDescriptor task, string robotCode, CancellationToken cancellationToken = default)
        {
            var taskJson = JsonConvert.SerializeObject(task);
            var content = await CompleteAsync(
                "Найди в коде робота решение для задачи. Верни JSON {\"xamlFragment\":\"...\",\"csharpCode\":\"...\"}.\n\n" +
                "Задача: " + taskJson + "\n\nКод робота:\n" + (robotCode ?? ""),
                cancellationToken).ConfigureAwait(false);

            var obj = ParseJson(content);
            return new SolutionRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                XamlFragment = obj.Value<string>("xamlFragment"),
                CSharpCode = obj.Value<string>("csharpCode"),
                Tasks = new List<TaskDescriptor> { task }
            };
        }

        public async Task<string> ChatAsync(string message, CancellationToken cancellationToken = default)
        {
            return await CompleteAsync(message, _options.ChatSystemPrompt, cancellationToken).ConfigureAwait(false);
        }

        internal async Task<string> CompleteAsync(string userMessage, CancellationToken cancellationToken)
        {
            return await CompleteAsync(userMessage, _options.SystemPrompt, cancellationToken).ConfigureAwait(false);
        }

        internal async Task<string> CompleteAsync(string userMessage, string systemPrompt, CancellationToken cancellationToken)
        {
            var request = BuildRequest(userMessage, systemPrompt);
            using (var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var obj = JObject.Parse(body);
                var content = obj["choices"]?[0]?["message"]?["content"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(content))
                    throw new InvalidOperationException("Пустой ответ от AI-провайдера.");
                return content;
            }
        }

        internal HttpRequestMessage BuildRequest(string userMessage)
        {
            return BuildRequest(userMessage, _options.SystemPrompt);
        }

        internal HttpRequestMessage BuildRequest(string userMessage, string systemPrompt)
        {
            var messages = new List<object>
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            };
            var payload = new
            {
                model = _options.Model,
                messages = messages,
                temperature = _options.Temperature
            };
            var json = JsonConvert.SerializeObject(payload);

            var url = (_options.BaseUrl ?? "https://api.openai.com/v1").TrimEnd('/') + "/chat/completions";
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            return request;
        }

        private static JObject ParseJson(string content)
        {
            return JObject.Parse(CleanContent(content));
        }

        private static JArray ParseArray(string content)
        {
            return JArray.Parse(CleanContent(content));
        }

        private static string CleanContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("Пустой JSON от AI-провайдера.");
            content = content.Trim();
            if (content.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewline = content.IndexOf('\n');
                var lastFence = content.LastIndexOf("```", StringComparison.Ordinal);
                if (lastFence > firstNewline)
                    content = content.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
            }
            return content;
        }

        private static ActivityNode ParseStep(JToken step)
        {
            return new ActivityNode
            {
                Kind = ParseKind(step.Value<string>("kind")),
                Name = step.Value<string>("name"),
                ClassName = step.Value<string>("className"),
                MethodName = step.Value<string>("methodName"),
                Variable = step.Value<string>("variable"),
                Expression = step.Value<string>("expression")
            };
        }

        private static ActivityNodeKind ParseKind(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind)) return ActivityNodeKind.InvokeMethod;
            switch (kind.Trim().ToLowerInvariant())
            {
                case "sequence": return ActivityNodeKind.Sequence;
                case "invokemethod": return ActivityNodeKind.InvokeMethod;
                case "assign": return ActivityNodeKind.Assign;
                case "writeline": return ActivityNodeKind.WriteLine;
                case "delay": return ActivityNodeKind.Delay;
                case "if": return ActivityNodeKind.If;
                case "while": return ActivityNodeKind.While;
                case "foreach": return ActivityNodeKind.ForEach;
                default: return ActivityNodeKind.InvokeMethod;
            }
        }

    }
}
