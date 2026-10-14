using System;
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
            Assert.That(reply, Does.Contain("привет"));
        }

        [Test]
        public async Task OpenAiProvider_ChatAsync_ReturnsContent()
        {
            var handler = new FakeHttpHandler { Responder = req => Completion("Привет, я агент!") };
            var provider = new OpenAiCompatibleProvider(new OpenAiOptions(), new HttpClient(handler));

            var reply = await provider.ChatAsync("здравствуй");

            Assert.That(reply, Is.EqualTo("Привет, я агент!"));
        }
    }
}
