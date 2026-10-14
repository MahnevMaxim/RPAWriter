using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Services;
using RPAWriter2.Services;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using System.IO;
using System.Collections.Generic;
using RPAWriter.Core.Models;

namespace RPAWriter2
{
    /// <summary>
    /// Единое окно расширения: чат с агентом и настройки.
    /// </summary>
    public partial class ToolWindow1Control : UserControl
    {
        private SettingsData _settings;
        private IAiProvider _ai;
        private SpecModel _currentSpec;

        public ToolWindow1Control()
        {
            this.InitializeComponent();
            LoadSettings();
            RecreateAiProvider();
            AppendChat("Агент готов. Провайдер: " + (_ai is StubAiProvider ? "заглушка (офлайн)" : "OpenAI-совместимый"));
        }

        private void LoadSettings()
        {
            _settings = SettingsStore.Load();
            BaseUrlBox.Text = string.IsNullOrWhiteSpace(_settings.BaseUrl) ? "https://api.openai.com/v1" : _settings.BaseUrl;
            ApiKeyBox.Password = _settings.ApiKey ?? "";
            ModelBox.Text = string.IsNullOrWhiteSpace(_settings.Model) ? "gpt-4o-mini" : _settings.Model;
            KbPathBox.Text = _settings.KnowledgeBasePath ?? "";
        }

        private void RecreateAiProvider()
        {
            _ai = AiProviderFactory.Create(_settings);
        }

        private string KbPath()
        {
            return (KbPathBox?.Text ?? "").Trim();
        }

        private string? RequireKbPath()
        {
            var p = KbPath();
            if (string.IsNullOrWhiteSpace(p))
            {
                AppendChat("Сначала укажите каталог базы знаний во вкладке «Настройки».");
                SetOutput("Не настроен каталог базы знаний. Укажите путь во вкладке «Настройки».");
                MainTabs.SelectedItem = SettingsTab;
                return null;
            }
            return p;
        }

        private void AppendChat(string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendChat(text)));
                return;
            }
            ChatBox.AppendText(text + Environment.NewLine);
            ChatBox.ScrollToEnd();
        }

        private void SetOutput(string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetOutput(text)));
                return;
            }
            OutputBox.Text = text;
        }

        private void ShowError(Exception ex)
        {
            AppendChat("Ошибка: " + ex.Message);
            SetOutput(ex.ToString());
        }

        private void ShowNotice(string message)
        {
            NoticeText.Text = message;
            NoticeBar.Visibility = Visibility.Visible;
        }

        private void HideNotice()
        {
            NoticeBar.Visibility = Visibility.Collapsed;
        }

#pragma warning disable VSTHRD100 // обработчики событий WPF — async void
        private async void OnSend(object sender, RoutedEventArgs e)
        {
            await SendMessageAsync();
        }

        private async void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            // Enter — отправить; Ctrl+Enter — перевод строки (обрабатывает сам TextBox)
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                await SendMessageAsync();
            }
        }

        private async void OnImportSpec(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Выберите ТЗ",
                Filter = "ТЗ (*.docx;*.xlsx;*.txt;*.md)|*.docx;*.xlsx;*.txt;*.md|Все файлы (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var spec = new OpenXmlDocumentReader().Read(dlg.FileName);
                var tasks = await _ai.DecomposeTasksAsync(spec.RawText);
                spec.Tasks = tasks.ToList();
                _currentSpec = spec;
                HideNotice();

                var sb = new StringBuilder();
                sb.AppendLine("ТЗ: " + spec.Name);
                sb.AppendLine();
                sb.AppendLine(spec.RawText);
                sb.AppendLine();
                sb.AppendLine("Задач: " + spec.Tasks.Count);
                foreach (var t in spec.Tasks)
                    sb.AppendLine("- " + (t.Action ?? "") + " → " + (t.Target ?? "") + " [" + (t.ActivityType ?? "") + "]");
                SetOutput(sb.ToString());

                AppendChat("Импортировано ТЗ: " + spec.Name + " (задач: " + spec.Tasks.Count + ")");
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private async void OnFindSimilar(object sender, RoutedEventArgs e)
        {
            if (_currentSpec == null) { ShowNotice("Сначала импортируйте ТЗ."); return; }
            try
            {
                var kbPath = RequireKbPath();
                if (kbPath == null) return;
                var kb = new FileKnowledgeBase(kbPath);
                var matches = new List<SimilarTaskMatch>();
                foreach (var task in _currentSpec.Tasks)
                    matches.AddRange(await kb.FindSimilarTasksAsync(task, 3));

                var sb = new StringBuilder();
                foreach (var m in matches)
                    sb.AppendLine("[" + m.Score.ToString("P0") + "] " + m.Solution.Name + " — " + string.Join("; ", m.Reasons));
                SetOutput(sb.Length == 0 ? "Похожих задач не найдено." : sb.ToString());
                AppendChat("Найдено похожих задач: " + matches.Count);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private async void OnGenerateRobot(object sender, RoutedEventArgs e)
        {
            if (_currentSpec == null) { ShowNotice("Сначала импортируйте ТЗ."); return; }
            try
            {
                var kbPath = RequireKbPath();
                if (kbPath == null) return;
                var kb = new FileKnowledgeBase(kbPath);
                var pipeline = new RpaPipelineService(new OpenXmlDocumentReader(), _ai, kb, new RobotGenerator());
                var result = await pipeline.GenerateRobotFromSpecAsync(_currentSpec.SourcePath);

                var dir = Path.GetDirectoryName(_currentSpec.SourcePath);
                var baseName = Path.GetFileNameWithoutExtension(_currentSpec.SourcePath);
                var xamlPath = Path.Combine(dir, baseName + ".xaml");
                var csPath = Path.Combine(dir, baseName + ".Actions.cs");
                File.WriteAllText(xamlPath, result.Xaml);
                File.WriteAllText(csPath, result.CSharpCode);

                SetOutput(result.Xaml + "\n\n--- C# ---\n\n" + result.CSharpCode);
                AppendChat("Робот сгенерирован и сохранён рядом с ТЗ: " + xamlPath + ", " + csPath);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private async void OnFormKnowledgeBase(object sender, RoutedEventArgs e)
        {
            var kbPath = RequireKbPath();
            if (kbPath == null) return;

            var dlg = new OpenFileDialog
            {
                Title = "Документация роботов",
                Filter = "Документация (*.docx;*.txt;*.md)|*.docx;*.txt;*.md|Все файлы (*.*)|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var kb = new FileKnowledgeBase(kbPath);
                var service = new KnowledgeBaseFormationService(new OpenXmlDocumentReader(), _ai, kb);
                var added = await service.BuildFromRobotDocsAsync(dlg.FileNames, "");
                SetOutput("Добавлено решений в базу знаний: " + added);
                AppendChat("База знаний пополнена: " + added + " решений.");
            }
            catch (Exception ex) { ShowError(ex); }
        }
#pragma warning restore VSTHRD100

        private async Task SendMessageAsync()
        {
            var text = InputBox.Text.Trim();
            if (text.Length == 0) return;
            InputBox.Clear();
            AppendChat("Вы: " + text);
            try
            {
                var reply = await _ai.ChatAsync(text);
                AppendChat("Агент: " + reply);
            }
            catch (Exception ex)
            {
                AppendChat("Ошибка: " + ex.Message);
            }
        }

        private void OnSaveSettings(object sender, RoutedEventArgs e)
        {
            _settings.BaseUrl = BaseUrlBox.Text.Trim();
            _settings.ApiKey = ApiKeyBox.Password.Trim();
            _settings.Model = ModelBox.Text.Trim();
            _settings.KnowledgeBasePath = KbPathBox.Text.Trim();
            SettingsStore.Save(_settings);
            RecreateAiProvider();
            AppendChat("Настройки сохранены. Провайдер: " + (_ai is StubAiProvider ? "заглушка (офлайн)" : "OpenAI-совместимый"));
        }
    }
}