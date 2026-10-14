namespace RPAWriter2
{
    /// <summary>Сообщение в чате агента: роль, текст и (опционально) «размышления» модели.</summary>
    public class ChatMessage
    {
        /// <summary>Роль: "user" | "agent" | "system".</summary>
        public string Role { get; set; } = "";
        public string Text { get; set; } = "";
        public string Reasoning { get; set; } = "";

        public bool HasReasoning => !string.IsNullOrWhiteSpace(Reasoning);

        public string Label
        {
            get
            {
                switch (Role)
                {
                    case "user": return "Вы: ";
                    case "agent": return "Агент: ";
                    default: return "";
                }
            }
        }

        public string DisplayText => string.IsNullOrEmpty(Label) ? (Text ?? "") : Label + (Text ?? "");
    }
}
