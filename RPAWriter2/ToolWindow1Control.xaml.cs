using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Services;
using RPAWriter2.Services;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using EnvDTE;
using EnvDTE80;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private IBuildFeedbackService _buildFeedback;
        private RobotTemplateService _robotTemplate;
        private DTE2? _dte;
        private readonly ObservableCollection<ChatMessage> _messages = new ObservableCollection<ChatMessage>();

        public ToolWindow1Control()
        {
            this.InitializeComponent();
            ChatList.ItemsSource = _messages;
            LoadSettings();
            RecreateAiProvider();
            _buildFeedback = new BuildFeedbackService();
            _robotTemplate = new RobotTemplateService();
            HookSolutionEvents();
            UpdateCreateRobotVisibility();
            AppendChat("Агент готов. Провайдер: " + (_ai is StubAiProvider ? "заглушка (офлайн)" : "OpenAI-совместимый"));
        }

        private void LoadSettings()
        {
            _settings = SettingsStore.Load();
            BaseUrlBox.Text = string.IsNullOrWhiteSpace(_settings.BaseUrl) ? "https://api.openai.com/v1" : _settings.BaseUrl;
            ApiKeyBox.Password = _settings.ApiKey ?? "";
            ModelBox.Text = string.IsNullOrWhiteSpace(_settings.Model) ? "gpt-4o-mini" : _settings.Model;
            KbPathBox.Text = _settings.KnowledgeBasePath ?? "";
            RobotsRootPathBox.Text = _settings.RobotsRootPath ?? "";
            RobotTemplateNameBox.Text = _settings.RobotTemplateName ?? "";
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
            AppendChatMessage(new ChatMessage { Role = "system", Text = text });
        }

        private void AppendChatMessage(ChatMessage message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendChatMessage(message)));
                return;
            }
            _messages.Add(message);
            ChatScroll.ScrollToEnd();
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

        private void HookSolutionEvents()
        {
            try
            {
                _dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
                if (_dte == null) return;
                var solEvents = _dte.Events.SolutionEvents;
                solEvents.Opened += () => UpdateCreateRobotVisibility();
                solEvents.AfterClosing += () => UpdateCreateRobotVisibility();
                solEvents.ProjectAdded += p => UpdateCreateRobotVisibility();
                solEvents.ProjectRemoved += p => UpdateCreateRobotVisibility();
            }
            catch
            {
                // События решения недоступны — видимость обновим при загрузке/по факту.
            }
        }

        private void UpdateCreateRobotVisibility()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(UpdateCreateRobotVisibility));
                return;
            }
            bool visible;
            try
            {
                var dte = _dte ?? (Package.GetGlobalService(typeof(SDTE)) as DTE2);
                visible = dte?.Solution == null || !dte.Solution.IsOpen;
            }
            catch
            {
                visible = true;
            }
            CreateRobotPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowImportProgress(string status)
        {
            ImportStatusText.Text = status;
            ImportProgressPanel.Visibility = Visibility.Visible;
        }

        private void SetImportStatus(string status)
        {
            ImportStatusText.Text = status;
        }

        private void HideImportProgress()
        {
            ImportProgressPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>Возвращает каталог открытого решения или null, если решение не открыто.</summary>
        private string? GetOpenSolutionDirectory()
        {
            try
            {
                var dte = _dte ?? (Package.GetGlobalService(typeof(SDTE)) as DTE2);
                if (dte?.Solution == null || !dte.Solution.IsOpen)
                    return null;
                var slnPath = dte.Solution.FullName;
                return string.IsNullOrWhiteSpace(slnPath) ? null : Path.GetDirectoryName(slnPath);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Копирует файл ТЗ в неизменном виде в папку docs в корне решения.</summary>
        private static void SaveImportedSpec(string sourcePath, string solutionDir)
        {
            var docsDir = Path.Combine(solutionDir, "docs");
            Directory.CreateDirectory(docsDir);
            File.Copy(sourcePath, Path.Combine(docsDir, Path.GetFileName(sourcePath)), overwrite: true);
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
            var solutionDir = GetOpenSolutionDirectory();
            if (solutionDir == null)
            {
                ShowNotice("Импорт ТЗ доступен только при открытом решении.");
                return;
            }

            var dlg = new OpenFileDialog
            {
                Title = "Выберите ТЗ",
                Filter = "ТЗ (*.docx;*.doc;*.xlsx;*.xls;*.txt;*.md)|*.docx;*.doc;*.xlsx;*.xls;*.txt;*.md|Все файлы (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            ShowImportProgress("Чтение файла…");
            try
            {
                SaveImportedSpec(dlg.FileName, solutionDir);

                var spec = new OpenXmlDocumentReader().Read(dlg.FileName);
                SetImportStatus("Разбор задач (AI)…");
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
            finally
            {
                HideImportProgress();
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
                Filter = "Документация (*.docx;*.doc;*.txt;*.md)|*.docx;*.doc;*.txt;*.md|Все файлы (*.*)|*.*",
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

        private async void OnBuildRelease(object sender, RoutedEventArgs e)
        {
            try
            {
                var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
                if (dte?.Solution == null || !dte.Solution.IsOpen)
                {
                    ShowNotice("Нет открытого решения. Откройте решение и повторите.");
                    return;
                }
                await _buildFeedback.BuildReleaseAsync();
                AppendChat("Сборка Release запущена.");
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnAnalyzeLogs(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Файлы логов",
                Filter = "Логи (*.log;*.txt;*.json)|*.log;*.txt;*.json|Все файлы (*.*)|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;

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

        private void OnConvertUiPath(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Робот UiPath",
                Filter = "UiPath workflow (*.xaml)|*.xaml|Все файлы (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

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
        private async void OnCreateRobot(object sender, RoutedEventArgs e)
        {
            var number = (RobotNumberBox?.Text ?? "").Trim();
            if (number.Length == 0 || !number.All(char.IsDigit))
            {
                AppendChat("Укажите номер робота (только цифры).");
                return;
            }

            var solutionName = "RPA-" + number;
            try
            {
                var result = await _robotTemplate.CreateRobotAsync(
                    solutionName, _settings.RobotsRootPath, _settings.RobotTemplateName,
                    progress: new Progress<string>(AppendChat));
                if (result.Success)
                    AppendChat("Робот создан: " + result.SolutionPath);
                else
                    AppendChat("Предупреждение: " + result.Message);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private void OnRobotNumberPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = e.Text.Any(c => !char.IsDigit(c));
        }

        private async void OnRefreshKnowledgeBase(object sender, RoutedEventArgs e)
        {
            var kbPath = RequireKbPath();
            if (kbPath == null) return;
            try
            {
                var kb = new FileKnowledgeBase(kbPath);
                var records = await kb.GetAllAsync();
                var items = records.Select(r => (r.Name ?? "<без имени>") + " — задач: " + (r.Tasks?.Count ?? 0)).ToList();
                KbListBox.ItemsSource = items;
                KbStatusText.Text = "Решений в базе знаний: " + records.Count;
            }
            catch (Exception ex) { ShowError(ex); }
        }

#pragma warning restore VSTHRD100

        private async Task SendMessageAsync()
        {
            var text = InputBox.Text.Trim();
            if (text.Length == 0) return;
            InputBox.Clear();
            AppendChatMessage(new ChatMessage { Role = "user", Text = text });
            try
            {
                var response = await _ai.ChatAsync(text);
                AppendChatMessage(new ChatMessage { Role = "agent", Text = response.Answer, Reasoning = response.Reasoning });
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
            _settings.RobotsRootPath = RobotsRootPathBox.Text.Trim();
            _settings.RobotTemplateName = RobotTemplateNameBox.Text.Trim();
            SettingsStore.Save(_settings);
            RecreateAiProvider();
            AppendChat("Настройки сохранены. Провайдер: " + (_ai is StubAiProvider ? "заглушка (офлайн)" : "OpenAI-совместимый"));
        }
    }
}