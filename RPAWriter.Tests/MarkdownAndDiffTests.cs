using System.Collections.Generic;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class MarkdownAndDiffTests
    {
        [Test]
        public void MarkdownContent_TasksToMarkdown_BuildsTable()
        {
            var spec = new SpecModel
            {
                Name = "Test",
                Tasks = new List<TaskDescriptor>
                {
                    new TaskDescriptor
                    {
                        Action = "открыть",
                        Target = "сайт",
                        ActivityType = "OpenBrowser",
                        Keywords = new List<string> { "браузер" }
                    }
                }
            };

            var md = MarkdownContent.TasksToMarkdown(spec);

            Assert.That(md, Does.Contain("# Задачи — Test"));
            Assert.That(md, Does.Contain("| Действие | Объект | Тип активности | Ключевые слова |"));
            Assert.That(md, Does.Contain("| открыть | сайт | OpenBrowser | браузер |"));
        }

        [Test]
        public void TextDiff_DetectsChanges()
        {
            var oldText = "Строка 1\nСтрока 2\nСтрока 3";
            var newText = "Строка 1\nСтрока 2 изменена\nСтрока 4";

            var diff = TextDiff.Diff(oldText, newText);

            Assert.That(diff.HasChanges, Is.True);
            Assert.That(diff.AddedCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(diff.RemovedCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(diff.Summary, Does.Contain("Расхождений"));
        }

        [Test]
        public void TextDiff_NoChanges()
        {
            var diff = TextDiff.Diff("a\nb\nc", "a\nb\nc");

            Assert.That(diff.HasChanges, Is.False);
            Assert.That(diff.Summary, Does.Contain("Расхождений не найдено"));
        }
    }
}
