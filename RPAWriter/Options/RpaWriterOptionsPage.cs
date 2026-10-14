#nullable disable
using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace RPAWriter.Options
{
    /// <summary>Страница настроек расширения RPAWriter.</summary>
    public class RpaWriterOptionsPage : DialogPage
    {
        [Category("AI-провайдер")]
        [DisplayName("Base URL")]
        [Description("Базовый URL OpenAI-совместимого API (например, https://api.openai.com/v1).")]
        public string BaseUrl { get; set; }

        [Category("AI-провайдер")]
        [DisplayName("API-ключ")]
        [Description("Ключ доступа к API.")]
        public string ApiKey { get; set; } = "";

        [Category("AI-провайдер")]
        [DisplayName("Модель")]
        [Description("Имя модели.")]
        public string Model { get; set; } = "gpt-4o-mini";

        [Category("База знаний")]
        [DisplayName("Каталог базы знаний")]
        [Description("Путь к каталогу с базой знаний (по умолчанию %AppData%\\RPAWriter\\KnowledgeBase).")]
        public string KnowledgeBasePath { get; set; } = "";
    }
}
