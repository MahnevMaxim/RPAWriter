using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
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
        public void NpoiDocumentReader_ReadsXlsText()
        {
            var path = Path.Combine(_dir, "spec.xls");
            Directory.CreateDirectory(_dir);
            CreateXls(path);

            var spec = new NpoiDocumentReader().Read(path);

            Assert.That(spec.RawText, Does.Contain("Лист1"));
            Assert.That(spec.RawText, Does.Contain("Открыть браузер"));
            Assert.That(spec.RawText, Does.Contain("Прочитать таблицу"));
            Assert.That(spec.Name, Is.EqualTo("spec"));
        }

        [Test]
        public void OpenXmlDocumentReader_ReadsXls()
        {
            var path = Path.Combine(_dir, "spec.xls");
            Directory.CreateDirectory(_dir);
            CreateXls(path);

            var spec = new OpenXmlDocumentReader().Read(path);

            Assert.That(spec.RawText, Does.Contain("Открыть браузер"));
            Assert.That(spec.RawText, Does.Contain("Лист1"));
        }

        [Test]
        public void NpoiDocumentReader_RejectsNonLegacyExtension()
        {
            var path = Path.Combine(_dir, "spec.docx");
            Directory.CreateDirectory(_dir);
            File.WriteAllText(path, "не legacy-документ");

            Assert.Throws<NotSupportedException>(() => new NpoiDocumentReader().Read(path));
        }

        [Test]
        public void NpoiDocumentReader_ExtractsDocText()
        {
            // Минимальный поток «WordDocument»: FIB-заголовок (fExtChar/Unicode) + текст UTF-16LE.
            var text = "Открыть браузер и прочитать таблицу";
            var textBytes = Encoding.Unicode.GetBytes(text);
            var data = new byte[0x0C + textBytes.Length];
            data[0x0B] = 0x10; // бит 12 слова flags (0x1000) — fExtChar
            Array.Copy(textBytes, 0, data, 0x0C, textBytes.Length);

            var extracted = NpoiDocumentReader.ExtractDocText(data);

            Assert.That(extracted, Does.Contain("Открыть браузер"));
            Assert.That(extracted, Does.Contain("прочитать таблицу"));
        }

        [Test]
        public void OpenXmlDocumentReader_RoutesDocToNpoi()
        {
            // .doc не является OLE2 — NPOI (POIFS) бросит своё исключение, а не «неподдерживаемый формат».
            var path = Path.Combine(_dir, "spec.doc");
            Directory.CreateDirectory(_dir);
            File.WriteAllText(path, "не валидный .doc");

            Exception caught = null;
            try { new OpenXmlDocumentReader().Read(path); }
            catch (Exception ex) { caught = ex; }

            Assert.That(caught, Is.Not.Null);
            Assert.That(caught, Is.Not.TypeOf<NotSupportedException>(), "ожидается ошибка NPOI при чтении невалидного .doc");
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

        private static void CreateXls(string path)
        {
            var workbook = new HSSFWorkbook();
            var sheet = workbook.CreateSheet("Лист1");
            var row = sheet.CreateRow(0);
            row.CreateCell(0).SetCellValue("Открыть браузер");
            row.CreateCell(1).SetCellValue("Прочитать таблицу");
            using (var fs = File.Create(path))
            {
                workbook.Write(fs);
            }
            workbook.Close();
        }
    }
}
