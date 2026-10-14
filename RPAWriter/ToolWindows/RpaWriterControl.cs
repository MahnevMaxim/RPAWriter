#nullable disable
#pragma warning disable VSTHRD100 // обработчики WinForms-кнопок — async void
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;
using RPAWriter.Core.Services;
using RPAWriter.Options;
using RPAWriter.Services;

namespace RPAWriter.ToolWindows
{
    /// <summary>Единое окно расширения: чат с агентом, все кнопки, настройки и предпросмотр.</summary>
    public class RpaWriterControl : UserControl
    {
        private TextBox _chatBox;
        private TextBox _inputBox;
        private TextBox _outputBox;
        private TextBox _baseUrlBox;
        private TextBox _apiKeyBox;
        private TextBox _modelBox;
        private TextBox _kbPathBox;

        private AsyncPackage _package;
        private RpaWriterOptionsPage _options;
        private IBuildFeedbackService _buildFeedback;

        private SpecModel _currentSpec;
        private IAiProvider _ai;

        public RpaWriterControl()
        {
            BuildUi();
        }

        public void Initialize(AsyncPackage package)
        {
            _package = package;
            _options = package.GetDialogPage(typeof(RpaWriterOptionsPage)) as RpaWriterOptionsPage;
            _buildFeedback = new BuildFeedbackService(package);
            LoadSettings();
            RecreateAiProvider();
            AppendChat("Агент готов. Провайдер: " + (_ai is StubAiProvider ? "заглушка (офлайн)" : "OpenAI-совместимый"));
        }

        private void BuildUi()
        {
            var chatLabel = new Label { Text = "Чат с агентом", Dock = DockStyle.Top, AutoSize = true };
            _chatBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Top, Height = 150 };

            var inputPanel = new Panel { Dock = DockStyle.Top, Height = 30 };
            _inputBox = new TextBox { Dock = DockStyle.Fill };
            var sendButton = new Button { Text = "Отправить", Dock = DockStyle.Right, Width = 90 };
            sendButton.Click += OnSend;
            inputPanel.Controls.Add(_inputBox);
            inputPanel.Controls.Add(sendButton);

            var buttonsPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            AddButton(buttonsPanel, "Импорт ТЗ", OnImportSpec);
            AddButton(buttonsPanel, "Поиск похожих", OnFindSimilar);
            AddButton(buttonsPanel, "Сгенерировать робота", OnGenerateRobot);
            AddButton(buttonsPanel, "Сформировать БЗ", OnFormKnowledgeBase);
            AddButton(buttonsPanel, "Собрать Release", OnBuildRelease);
            AddButton(buttonsPanel, "Анализ логов", OnAnalyzeLogs);
            AddButton(buttonsPanel, "UiPath → WF", OnConvertUiPath);

            var settingsGroup = new GroupBox { Text = "Настройки", Dock = DockStyle.Top, AutoSize = true };
            var settingsTable = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(6) };
            _baseUrlBox = AddLabeledField(settingsTable, 0, "Base URL", "https://api.openai.com/v1", false);
            _apiKeyBox = AddLabeledField(settingsTable, 1, "API-ключ", "", true);
            _modelBox = AddLabeledField(settingsTable, 2, "Модель", "gpt-4o-mini", false);
            _kbPathBox = AddLabeledField(settingsTable, 3, "Каталог БЗ", "", false);
            var saveSettings = new Button { Text = "Сохранить настройки", AutoSize = true };
            saveSettings.Click += OnSaveSettings;
            settingsTable.Controls.Add(saveSettings, 1, 4);
            settingsGroup.Controls.Add(settingsTable);

            var outputLabel = new Label { Text = "Результат", Dock = DockStyle.Top, AutoSize = true };
            _outputBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill };

            Controls.Add(_outputBox);
            Controls.Add(outputLabel);
            Controls.Add(settingsGroup);
            Controls.Add(buttonsPanel);
            Controls.Add(inputPanel);
            Controls.Add(_chatBox);
            Controls.Add(chatLabel);
        }

        private string KbPath()
        {
            var p = (_kbPathBox?.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(p))
                p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RPAWriter", "KnowledgeBase");
            return p;
        }

        private void LoadSettings()
        {
            if (_options == null) return;
            _baseUrlBox.Text = string.IsNullOrWhiteSpace(_options.BaseUrl) ? "https://api.openai.com/v1" : _options.BaseUrl;
            _apiKeyBox.Text = _options.ApiKey ?? "";
            _modelBox.Text = string.IsNullOrWhiteSpace(_options.Model) ? "gpt-4o-mini" : _options.Model;
            _kbPathBox.Text = _options.KnowledgeBasePath ?? "";
        }

        private void RecreateAiProvider()
        {
            var apiKey = (_apiKeyBox?.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _ai = new StubAiProvider();
            }
            else
            {
                _ai = new OpenAiCompatibleProvider(new OpenAiOptions
                {
                    BaseUrl = string.IsNullOrWhiteSpace(_baseUrlBox?.Text) ? "https://api.openai.com/v1" : _baseUrlBox.Text.Trim(),
                    ApiKey = apiKey,
                    Model = string.IsNullOrWhiteSpace(_modelBox?.Text) ? "gpt-4o-mini" : _modelBox.Text.Trim()
                });
            }
        }

        private void AppendChat(string text)
        {
            void Do() => _chatBox.AppendText(text + Environment.NewLine);
            if (_chatBox.InvokeRequired) _chatBox.BeginInvoke((Action)Do);
            else Do();
        }

        private void SetOutput(string text)
        {
            void Do() => _outputBox.Text = text;
            if (_outputBox.InvokeRequired) _outputBox.BeginInvoke((Action)Do);
            else Do();
        }

        private void ShowError(Exception ex)
        {
            AppendChat("Ошибка: " + ex.Message);
            SetOutput(ex.ToString());
        }

        private static Button AddButton(FlowLayoutPanel panel, string text, EventHandler handler)
        {
            var b = new Button { Text = text, AutoSize = true };
            b.Click += handler;
            panel.Controls.Add(b);
            return b;
        }

        private static TextBox AddLabeledField(TableLayoutPanel table, int row, string label, string value, bool password)
        {
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            var box = new TextBox { Text = value, Anchor = AnchorStyles.Left | AnchorStyles.Right, Width = 320, UseSystemPasswordChar = password };
            table.Controls.Add(box, 1, row);
            return box;
        }

        private async void OnImportSpec(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog { Title = "Выберите ТЗ", Filter = "ТЗ (*.docx;*.xlsx;*.txt;*.md)|*.docx;*.xlsx;*.txt;*.md|Все файлы (*.*)|*.*" })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    _currentSpec = new OpenXmlDocumentReader().Read(dlg.FileName);
                    _currentSpec.Tasks = (await _ai.DecomposeTasksAsync(_currentSpec.RawText)).ToList();
                    SetOutput(_currentSpec.RawText);
                    AppendChat("Импортировано ТЗ: " + _currentSpec.Name + " (задач: " + _currentSpec.Tasks.Count + ")");
                }
                catch (Exception ex) { ShowError(ex); }
            }
        }

        private async void OnFindSimilar(object sender, EventArgs e)
        {
            if (_currentSpec == null) { AppendChat("Сначала импортируйте ТЗ."); return; }
            try
            {
                var kb = new FileKnowledgeBase(KbPath());
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

        private async void OnGenerateRobot(object sender, EventArgs e)
        {
            if (_currentSpec == null) { AppendChat("Сначала импортируйте ТЗ."); return; }
            try
            {
                var kb = new FileKnowledgeBase(KbPath());
                var pipeline = new RpaPipelineService(new OpenXmlDocumentReader(), _ai, kb, new RobotGenerator());
                var result = await pipeline.GenerateRobotFromSpecAsync(_currentSpec.SourcePath);
                var dir = Path.GetDirectoryName(_currentSpec.SourcePath);
                var baseName = Path.GetFileNameWithoutExtension(_currentSpec.SourcePath);
                File.WriteAllText(Path.Combine(dir, baseName + ".xaml"), result.Xaml);
                File.WriteAllText(Path.Combine(dir, baseName + ".Actions.cs"), result.CSharpCode);
                SetOutput(result.Xaml + "\n\n--- C# ---\n\n" + result.CSharpCode);
                AppendChat("Робот сгенерирован и сохранён рядом с ТЗ.");
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private async void OnFormKnowledgeBase(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog { Title = "Документация роботов", Filter = "Документация (*.docx;*.txt;*.md)|*.docx;*.txt;*.md|Все файлы (*.*)|*.*", Multiselect = true })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    var kb = new FileKnowledgeBase(KbPath());
                    var service = new KnowledgeBaseFormationService(new OpenXmlDocumentReader(), _ai, kb);
                    var added = await service.BuildFromRobotDocsAsync(dlg.FileNames, "");
                    SetOutput("Добавлено решений в базу знаний: " + added);
                    AppendChat("База знаний пополнена: " + added + " решений.");
                }
                catch (Exception ex) { ShowError(ex); }
            }
        }

        private async void OnBuildRelease(object sender, EventArgs e)
        {
            try
            {
                await _buildFeedback.BuildReleaseAsync();
                AppendChat("Сборка Release запущена.");
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnAnalyzeLogs(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog { Title = "Файлы логов", Filter = "Логи (*.log;*.txt;*.json)|*.log;*.txt;*.json|Все файлы (*.*)|*.*", Multiselect = true })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    var lines = new List<string>();
                    foreach (var f in dlg.FileNames) lines.AddRange(File.ReadAllLines(f));
                    var analysis = new LogAnalyzer().Analyze(lines);
                    SetOutput(analysis.Summary);
                    foreach (var err in analysis.Errors.Take(20)) AppendChat("ERR: " + err.Message);
                }
                catch (Exception ex) { ShowError(ex); }
            }
        }

        private void OnConvertUiPath(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog { Title = "Робот UiPath", Filter = "UiPath workflow (*.xaml)|*.xaml|Все файлы (*.*)|*.*" })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    var xaml = File.ReadAllText(dlg.FileName);
                    var result = new UiPathToWfConverter(new RobotGenerator()).Convert(xaml);
                    var dir = Path.GetDirectoryName(dlg.FileName);
                    var baseName = Path.GetFileNameWithoutExtension(dlg.FileName);
                    File.WriteAllText(Path.Combine(dir, baseName + ".wf.xaml"), result.Xaml);
                    File.WriteAllText(Path.Combine(dir, baseName + ".wf.cs"), result.CSharpCode);
                    SetOutput(result.Xaml + "\n\n--- C# ---\n\n" + result.CSharpCode);
                    AppendChat("Конвертация завершена. Предупреждений: " + result.Warnings.Count);
                }
                catch (Exception ex) { ShowError(ex); }
            }
        }

        private async void OnSend(object sender, EventArgs e)
        {
            var text = _inputBox.Text.Trim();
            if (text.Length == 0) return;
            _inputBox.Clear();
            AppendChat("Вы: " + text);
            try
            {
                var reply = await _ai.ChatAsync(text);
                AppendChat("Агент: " + reply);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnSaveSettings(object sender, EventArgs e)
        {
            if (_options == null) return;
            _options.BaseUrl = _baseUrlBox.Text.Trim();
            _options.ApiKey = _apiKeyBox.Text.Trim();
            _options.Model = _modelBox.Text.Trim();
            _options.KnowledgeBasePath = _kbPathBox.Text.Trim();
            _options.SaveSettingsToStorage();
            RecreateAiProvider();
            AppendChat("Настройки сохранены.");
        }


    }
}
