using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    public class FakeHttpHandler : HttpMessageHandler
    {
        public HttpRequestMessage LastRequest { get; private set; }
        public string LastBody { get; private set; }
        public Func<HttpRequestMessage, string> Responder { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content != null ? await request.Content.ReadAsStringAsync() : "";
            var content = Responder?.Invoke(request) ?? "";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
        }
    }

    /// <summary>Имитирует «зависший» провайдер: запрос не завершается, пока его не отменят (пользователем или таймаутом).</summary>
    public class BlockingHttpHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    [TestFixture]
    public class ProviderTests
    {
        private static string Completion(string content)
        {
            var payload = new
            {
                choices = new[] { new { message = new { content = content } } }
            };
            return JsonSerializer.Serialize(payload);
        }

        [Test]
        public async Task AnalyzeSpecAsync_SendsRequestToChatCompletionsAndParses()
        {
            var handler = new FakeHttpHandler
            {
                Responder = req => Completion(
                    "{\"summary\":\"s\",\"tasks\":[{\"action\":\"открыть\",\"target\":\"сайт\",\"activityType\":\"OpenBrowser\"}]}")
            };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions
            {
                BaseUrl = "https://example.com/v1",
                ApiKey = "sk-test",
                Model = "test-model"
            }, new HttpClient(handler));

            var analysis = await provider.AnalyzeSpecAsync(new SpecModel { RawText = "Открыть сайт" });

            Assert.That(handler.LastRequest.RequestUri.ToString(), Is.EqualTo("https://example.com/v1/chat/completions"));
            Assert.That(handler.LastRequest.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(handler.LastRequest.Headers.Authorization.Scheme, Is.EqualTo("Bearer"));
            Assert.That(handler.LastRequest.Headers.Authorization.Parameter, Is.EqualTo("sk-test"));
            Assert.That(handler.LastBody, Does.Contain("test-model"));

            Assert.That(analysis.Summary, Is.EqualTo("s"));
            Assert.That(analysis.Tasks.Count, Is.EqualTo(1));
            Assert.That(analysis.Tasks[0].ActivityType, Is.EqualTo("OpenBrowser"));
        }

        [Test]
        public async Task GenerateRobotAsync_ParsesSteps()
        {
            var handler = new FakeHttpHandler
            {
                Responder = req => Completion(
                    "{\"name\":\"Robot\",\"steps\":[" +
                    "{\"kind\":\"InvokeMethod\",\"className\":\"RpaActions\",\"methodName\":\"OpenBrowser\"}," +
                    "{\"kind\":\"Assign\",\"variable\":\"url\",\"expression\":\"http://x\"}]}")
            };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions(), new HttpClient(handler));

            var plan = await provider.GenerateRobotAsync(new SpecAnalysis());

            Assert.That(plan.Name, Is.EqualTo("Robot"));
            Assert.That(plan.Root.Children.Count, Is.EqualTo(2));
            Assert.That(plan.Root.Children[0].MethodName, Is.EqualTo("OpenBrowser"));
            Assert.That(plan.Root.Children[1].Kind, Is.EqualTo(ActivityNodeKind.Assign));
        }

        [Test]
        public async Task StubAiProvider_DecomposeTasks_CreatesTasks()
        {
            var provider = new StubAiProvider();
            var tasks = await provider.DecomposeTasksAsync("Открыть браузер. Прочитать таблицу из файла.");

            Assert.That(tasks.Count, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public async Task StubAiProvider_ChatAsync_ReturnsText()
        {
            var reply = await new StubAiProvider().ChatAsync("привет");
            Assert.That(reply.Answer, Does.Contain("привет"));
            Assert.That(reply.Reasoning, Is.Empty);
        }

        [Test]
        public async Task OpenAiProvider_ChatAsync_ReturnsContent()
        {
            var handler = new FakeHttpHandler { Responder = req => Completion("Привет, я агент!") };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions(), new HttpClient(handler));

            var reply = await provider.ChatAsync("здравствуй");

            Assert.That(reply.Answer, Is.EqualTo("Привет, я агент!"));
            Assert.That(reply.Reasoning, Is.Null.Or.Empty);
        }

        [Test]
        public async Task OpenAiProvider_ChatAsync_ParsesReasoning()
        {
            var payload = new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            content = "Ответ",
                            reasoning_content = "Размышление"
                        }
                    }
                }
            };
            var handler = new FakeHttpHandler { Responder = req => JsonSerializer.Serialize(payload) };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions(), new HttpClient(handler));

            var reply = await provider.ChatAsync("запрос");

            Assert.That(reply.Answer, Is.EqualTo("Ответ"));
            Assert.That(reply.Reasoning, Is.EqualTo("Размышление"));
        }

        [Test]
        public void OpenAiProvider_ChatAsync_CancelsWhenTokenIsCancelled()
        {
            var handler = new BlockingHttpHandler();
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions(), new HttpClient(handler));
            using var cts = new CancellationTokenSource();

            var pending = provider.ChatAsync("привет", cts.Token);
            cts.Cancel();

            Assert.ThrowsAsync(Is.InstanceOf<OperationCanceledException>(), async () => await pending);
        }

        [Test]
        public void OpenAiProvider_ChatAsync_ThrowsTimeoutWhenProviderHangs()
        {
            var handler = new BlockingHttpHandler();
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions { TimeoutSeconds = 1 }, new HttpClient(handler));

            Assert.ThrowsAsync<TimeoutException>(async () => await provider.ChatAsync("привет"));
        }

        [Test]
        public async Task AnalyzeSpecAsync_IncludesRulesFromFolder()
        {
            var dir = Path.Combine(Path.GetTempPath(), "rpawriter_rules_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "rules.md"), "Правило: используй InvokeMethod для всех действий.");
                var handler = new FakeHttpHandler
                {
                    Responder = req => Completion("{\"summary\":\"s\",\"tasks\":[]}")
                };
                var provider = new OpenAiCompatibleProvider(new OpenAiOptions
                {
                    BaseUrl = "https://example.com/v1",
                    ApiKey = "sk-test",
                    Model = "test-model",
                    RulesPath = dir
                }, new HttpClient(handler));

                await provider.AnalyzeSpecAsync(new SpecModel { RawText = "Открыть сайт" });

                Assert.That(handler.LastBody, Does.Contain("Правило: используй InvokeMethod"));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Test]
        public async Task DecomposeTasksAsync_IncludesRulesFromFolder()
        {
            var dir = Path.Combine(Path.GetTempPath(), "rpawriter_rules_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "rules.md"), "Правило: одна задача — одно действие.");
                var handler = new FakeHttpHandler { Responder = req => Completion("[{\"action\":\"открыть\"}]") };
                var provider = new OpenAiCompatibleProvider(new OpenAiOptions { RulesPath = dir }, new HttpClient(handler));

                await provider.DecomposeTasksAsync("Открыть браузер");

                Assert.That(handler.LastBody, Does.Contain("Правило: одна задача — одно действие."));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
