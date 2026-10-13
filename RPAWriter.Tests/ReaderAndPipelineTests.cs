using System.IO;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using NUnit.Framework;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class ReaderAndPipelineTests
    {
        private string _dir;

        [SetUp]
        public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "RPAWriterTests", Path.GetRandomFileName());

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void OpenXmlDocumentReader_ReadsWordText()
        {
            var path = Path.Combine(_dir, "spec.docx");
            Directory.CreateDirectory(_dir);
            CreateDocx(path, "Открыть браузер", "Прочитать таблицу");

            var spec = new OpenXmlDocumentReader().Read(path);

            Assert.That(spec.RawText, Does.Contain("Открыть браузер"));
            Assert.That(spec.RawText, Does.Contain("Прочитать таблицу"));
            Assert.That(spec.Name, Is.EqualTo("spec"));
        }

        [Test]
        public async Task Pipeline_GeneratesXamlAndCode()
        {
            var path = Path.Combine(_dir, "spec.txt");
            Directory.CreateDirectory(_dir);
            File.WriteAllText(path, "Открыть браузер и прочитать таблицу.");

            var kb = new FileKnowledgeBase(_dir);
            var pipeline = new RpaPipelineService(
                new OpenXmlDocumentReader(),
                new StubAiProvider(),
                kb,
                new RobotGenerator());

            var result = await pipeline.GenerateRobotFromSpecAsync(path);

            Assert.That(result.Xaml, Does.Contain("InvokeMethod"));
            Assert.That(result.CSharpCode, Does.Not.Empty);
        }

        [Test]
        public async Task KnowledgeBaseFormation_BuildsRecords()
        {
            var docPath = Path.Combine(_dir, "robot.md");
            Directory.CreateDirectory(_dir);
            File.WriteAllText(docPath, "Открыть браузер. Заполнить форму.");

            var kb = new FileKnowledgeBase(_dir);
            var service = new KnowledgeBaseFormationService(new OpenXmlDocumentReader(), new StubAiProvider(), kb);

            var added = await service.BuildFromRobotDocsAsync(new[] { docPath }, "// robot code");

            Assert.That(added, Is.GreaterThan(0));
            var all = await kb.GetAllAsync();
            Assert.That(all.Count, Is.EqualTo(added));
        }

        private static void CreateDocx(string path, params string[] paragraphs)
        {
            using (var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body());
                foreach (var text in paragraphs)
                    main.Document.Body.AppendChild(new Paragraph(new Run(new Text(text))));
                main.Document.Save();
            }
        }
    }
}
