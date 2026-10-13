using System.Collections.Generic;
using NUnit.Framework;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;

namespace RPAWriter.Tests
{
    [TestFixture]
    public class SimilarityIndexTests
    {
        private SimilarityIndex _index;
        private List<SolutionRecord> _records;

        [SetUp]
        public void SetUp()
        {
            _index = new SimilarityIndex();
            _records = new List<SolutionRecord>
            {
                new SolutionRecord
                {
                    Id = "1",
                    Name = "Открыть сайт и прочитать таблицу",
                    Tasks = new List<TaskDescriptor>
                    {
                        new TaskDescriptor { Action = "открыть", Target = "страницу браузера", ActivityType = "OpenBrowser", Keywords = new List<string>{ "браузер", "страница" } },
                        new TaskDescriptor { Action = "прочитать", Target = "таблицу", ActivityType = "ReadTable", Keywords = new List<string>{ "таблица", "данные" } }
                    }
                },
                new SolutionRecord
                {
                    Id = "2",
                    Name = "Заполнить форму",
                    Tasks = new List<TaskDescriptor>
                    {
                        new TaskDescriptor { Action = "заполнить", Target = "форму", ActivityType = "FillField", Keywords = new List<string>{ "форма", "поле" } }
                    }
                }
            };
        }

        [Test]
        public void FindSimilar_ReturnsMatchingRecord()
        {
            var query = new TaskDescriptor
            {
                Action = "прочитать",
                Target = "таблицу",
                ActivityType = "ReadTable",
                Keywords = new List<string> { "таблица", "данные" }
            };

            var result = _index.FindSimilar(query, _records, 3);

            Assert.That(result.Count, Is.GreaterThan(0));
            Assert.That(result[0].Solution.Id, Is.EqualTo("1"));
            Assert.That(result[0].Score, Is.GreaterThan(0));
            Assert.That(result[0].Reasons, Is.Not.Empty);
        }

        [Test]
        public void FindSimilar_RespectsTopK()
        {
            var query = new TaskDescriptor { Action = "выполнить", Target = "что-то", ActivityType = "Custom" };

            var result = _index.FindSimilar(query, _records, 1);

            Assert.That(result.Count, Is.LessThanOrEqualTo(1));
        }

        [Test]
        public void FindSimilar_NoRecords_ReturnsEmpty()
        {
            var query = new TaskDescriptor { Action = "открыть" };
            var result = _index.FindSimilar(query, new List<SolutionRecord>(), 3);
            Assert.That(result, Is.Empty);
        }
    }
}
