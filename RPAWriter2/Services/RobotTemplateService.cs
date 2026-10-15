using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace RPAWriter2.Services
{
    /// <summary>Результат создания решения робота из шаблона.</summary>
    public class RobotCreateResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string? SolutionPath { get; set; }
    }

    /// <summary>
    /// Создание решения робота из установленного шаблона решения (название шаблона задаётся в настройках).
    /// Имя решения = «RPA-&lt;номер&gt;». Работает только когда не открыто ни одного решения.
    /// </summary>
    public class RobotTemplateService
    {
        /// <summary>GUID окна Package Manager Console (PowerConsoleToolWindow).</summary>
        private static readonly Guid PowerConsoleWindowGuid = new Guid("0AD07096-BBA9-4900-A651-0598D26F6D24");

        /// <summary>Сколько времени ждать готовности PowerShell-хоста Package Manager Console.</summary>
        private static readonly TimeSpan PowerConsoleReadyTimeout = TimeSpan.FromSeconds(30);

        /// <summary>Сколько проектов должно появиться в решении после развёртывания шаблона.</summary>
        private const int ExpectedProjectCount = 2;

        /// <summary>Сколько времени ждать появления всех проектов решения.</summary>
        private static readonly TimeSpan ProjectWaitTimeout = TimeSpan.FromSeconds(120);

        /// <summary>Kind папки решения (solution folder) в EnvDTE.</summary>
        private const string SolutionFolderKind = "{66A26722-8FB5-11D2-AA7E-00C04F688DDE}";

        public async Task<RobotCreateResult> CreateRobotAsync(
            string solutionName, string rootPath, string templateName,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            if (dte == null)
                return Fail("Не удалось получить доступ к Visual Studio (DTE).");

            var solution = dte.Solution;
            if (solution == null)
                return Fail("Не удалось получить доступ к решению (Solution).");
            if (solution.IsOpen)
                return Fail("Сначала закройте открытое решение.");

            var root = ResolveRoot(dte, rootPath);
            var destDir = Path.Combine(root, solutionName);
            var slnFile = Path.Combine(destDir, solutionName + ".sln");

            // Защита от перезаписи существующего решения.
            if (Directory.Exists(destDir) || File.Exists(slnFile))
                return Fail("Решение \"" + solutionName + "\" уже существует: " + destDir);

            if (string.IsNullOrWhiteSpace(templateName))
                return Fail("Не задано название шаблона робота (настройка «Название файла шаблона робота»).");

            var name = templateName.Trim();
            Report(progress, "Поиск шаблона «" + name + "»…");

            var templatePath = FindTemplate(name, progress);
            if (templatePath == null)
                return Fail("Шаблон решения \"" + name + "\" не найден. Установите шаблон и повторите.");

            var vstPath = ResolveVstemplate(templatePath, progress);
            if (vstPath == null)
                return Fail("В шаблоне \"" + name + "\" не найден файл .vstemplate.");

            try
            {
                Directory.CreateDirectory(destDir);
                solution.Create(destDir, solutionName);
                Report(progress, "Создаю проект из шаблона: " + vstPath);
                solution.AddFromTemplate(vstPath, destDir, solutionName, false);
                solution.SaveAs(slnFile);
            }
            catch (Exception ex)
            {
                return Fail("Не удалось создать решение из шаблона: " + ex.Message);
            }

            // Ждём, пока шаблон полностью развернётся в решении (по умолчанию 2 проекта),
            // иначе reinstall может затронуть не все проекты.
            await WaitForProjectsAsync(dte, ExpectedProjectCount, progress, cancellationToken);

            // После создания решения переустанавливаем пакеты NuGet.
            var message = "Решение создано.";
            try
            {
                await RunPackageManagerCommandAsync("Update-Package -Reinstall", progress, cancellationToken);
            }
            catch (Exception ex)
            {
                message += " Не удалось выполнить Update-Package -Reinstall: " + ex.Message;
            }
            return new RobotCreateResult { Success = true, Message = message, SolutionPath = slnFile };
        }

        private static RobotCreateResult Fail(string message) =>
            new RobotCreateResult { Success = false, Message = message };

        private static string ResolveRoot(DTE2 dte, string rootPath)
        {
            if (!string.IsNullOrWhiteSpace(rootPath))
                return rootPath.Trim();

            // Пытаемся прочитать «каталог проектов по умолчанию» из VS.
            try
            {
                var props = dte?.get_Properties("Environment", "ProjectsAndSolution");
                var value = props?.Item("ProjectsLocation")?.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value!;
            }
            catch
            {
                // Свойство может быть недоступно, пока страница параметров не открывалась.
            }

            // Fallback: стандартный каталог VS 2017+.
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "repos");
        }

        private static string? FindTemplate(string name, IProgress<string>? progress)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return null;

            var roots = new List<string>();

            // Пользовательские шаблоны: %USERPROFILE%\Documents\Visual Studio <ver>\Templates\ProjectTemplates
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            foreach (var vsDir in SafeEnumerateDirectories(docs, "Visual Studio*"))
            {
                var pt = Path.Combine(vsDir, "Templates", "ProjectTemplates");
                if (Directory.Exists(pt)) roots.Add(pt);
            }

            // Шаблоны из установки VS: <VSInstall>\Common7\IDE\ProjectTemplates
            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            var devenvDir = Path.GetDirectoryName(dte?.FullName ?? "");
            if (!string.IsNullOrEmpty(devenvDir))
            {
                var ide = Path.Combine(devenvDir, "ProjectTemplates");
                if (Directory.Exists(ide)) roots.Add(ide);
            }

            Report(progress, "Каталоги для поиска: " + (roots.Count == 0 ? "не найдены" : string.Join("; ", roots)));

            // Имя может быть задано с расширением (.zip/.vstemplate) или без него.
            var hasExtension = name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                               || name.EndsWith(".vstemplate", StringComparison.OrdinalIgnoreCase);
            var baseName = hasExtension ? Path.GetFileNameWithoutExtension(name) : name;

            foreach (var root in roots)
            {
                Report(progress, "Проверяю каталог: " + root);

                // 1) Прямой файл-шаблон (.vstemplate или .zip) с подходящим именем (рекурсивно).
                foreach (var f in SafeEnumerateFiles(root, "*.vstemplate", SearchOption.AllDirectories))
                    if (MatchesName(f, name, baseName, hasExtension))
                    {
                        Report(progress, "Найден файл шаблона: " + f);
                        return f;
                    }
                foreach (var f in SafeEnumerateFiles(root, "*.zip", SearchOption.AllDirectories))
                    if (MatchesName(f, name, baseName, hasExtension))
                    {
                        Report(progress, "Найден файл шаблона: " + f);
                        return f;
                    }

                // 2) Каталог с именем шаблона, внутри которого лежит .vstemplate (или .zip).
                foreach (var dir in SafeEnumerateDirectories(root, baseName, SearchOption.AllDirectories))
                {
                    foreach (var vst in SafeEnumerateFiles(dir, "*.vstemplate", SearchOption.TopDirectoryOnly))
                    {
                        Report(progress, "Найден файл шаблона: " + vst);
                        return vst;
                    }
                    foreach (var zip in SafeEnumerateFiles(dir, "*.zip", SearchOption.TopDirectoryOnly))
                    {
                        Report(progress, "Найден файл шаблона: " + zip);
                        return zip;
                    }
                }
            }
            return null;
        }

        private static void Report(IProgress<string>? progress, string message)
        {
            progress?.Report(message);
        }

        /// <summary>
        /// Возвращает путь к файлу .vstemplate. Если найден шаблон-архив (.zip),
        /// распаковывает его во временный каталог и возвращает .vstemplate изнутри.
        /// </summary>
        private static string? ResolveVstemplate(string templatePath, IProgress<string>? progress)
        {
            if (templatePath.EndsWith(".vstemplate", StringComparison.OrdinalIgnoreCase))
                return templatePath;

            if (!templatePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return null;

            Report(progress, "Шаблон упакован в .zip, распаковываю: " + templatePath);

            var extractDir = Path.Combine(Path.GetTempPath(), "RPAWriter2", "Templates",
                Path.GetFileNameWithoutExtension(templatePath));
            try
            {
                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, true);
                Directory.CreateDirectory(extractDir);
                ZipFile.ExtractToDirectory(templatePath, extractDir);

                var vst = Directory.EnumerateFiles(extractDir, "*.vstemplate", SearchOption.AllDirectories)
                                   .FirstOrDefault();
                if (vst != null)
                    Report(progress, "Извлечён файл .vstemplate: " + vst);
                return vst;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Запускает команду в Package Manager Console (например «Update-Package -Reinstall»).
        /// Основной путь — через канонический IPowerConsoleService (окно PMC), который корректно
        /// ставит команду в очередь, пока PowerShell-хост ещё инициализируется. Если сервис
        /// получить не удалось — фолбэк на прежний dte.ExecuteCommand с аргументом.
        /// </summary>
        public async Task RunPackageManagerCommandAsync(
            string command, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // DTE именно текущего экземпляра VS (а не «активного» объекта через GetActiveObject),
            // чтобы команда не ушла в другой открытый экземпляр Visual Studio.
            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2
                ?? throw new InvalidOperationException("Не удалось получить доступ к Visual Studio (DTE).");

            // Активируем главное окно VS, чтобы консоль гарантированно была на переднем плане.
            try { dte.MainWindow?.Activate(); }
            catch { /* окно могло быть недоступно */ }

            // Открываем/инициализируем Package Manager Console.
            try { dte.ExecuteCommand("View.PackageManagerConsole"); }
            catch { /* окно могло быть уже открыто */ }

            Report(progress, "Ожидаю готовность Package Manager Console…");

            // Основной путь: канонический IPowerConsoleService (окно PMC).
            if (await TrySendViaPowerConsoleAsync(command, cancellationToken))
            {
                Report(progress, "Команда отправлена через IPowerConsoleService: " + command);
                return;
            }

            // Фолбэк: прежний рабочий вариант через dte.ExecuteCommand.
            Report(progress, "IPowerConsoleService недоступен — использую dte.ExecuteCommand (фолбэк).");

            await Task.Delay(3000, cancellationToken).ConfigureAwait(false);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            try
            {
                dte.ExecuteCommand("View.PackageManagerConsole", command);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Не удалось отправить команду в Package Manager Console: " + ex.Message, ex);
            }

            Report(progress, "Команда отправлена через dte.ExecuteCommand (фолбэк) и выполняется: " + command);
        }

        /// <summary>
        /// Пытается отправить команду через канонический IPowerConsoleService (окно PMC).
        /// Дожидается инициализации PowerShell-хоста и возвращает true, если команда принята.
        /// </summary>
        private static async Task<bool> TrySendViaPowerConsoleAsync(string command, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + PowerConsoleReadyTimeout;

            while (true)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                var console = ResolvePowerConsoleWindow();
                if (console == null)
                {
                    if (DateTime.UtcNow > deadline)
                        return false;
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var execute = console.GetType().GetMethod(
                    "Execute",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(string), typeof(object[]) },
                    null);
                if (execute == null)
                    return false;

                var isInitialized = console.GetType().GetMethod(
                    "IsHostSuccessfullyInitialized",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    Type.EmptyTypes,
                    null);

                bool ready = true;
                if (isInitialized != null)
                {
                    try { ready = (bool)(isInitialized.Invoke(console, null) ?? false); }
                    catch { ready = false; }
                }

                if (ready)
                {
                    bool accepted;
                    try
                    {
                        accepted = (bool)(execute.Invoke(console, new object[] { command, new object[0] }) ?? false);
                    }
                    catch
                    {
                        return false;
                    }

                    if (accepted)
                        return true;
                }

                if (DateTime.UtcNow > deadline)
                    return false;

                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>Находит окно Package Manager Console и возвращает его объект (IPowerConsoleService).</summary>
        private static object? ResolvePowerConsoleWindow()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var uiShell = Package.GetGlobalService(typeof(SVsUIShell)) as IVsUIShell;
                if (uiShell == null)
                    return null;

                var slot = PowerConsoleWindowGuid;
                IVsWindowFrame? frame = null;
                int hr = uiShell.FindToolWindow((uint)__VSFINDTOOLWIN.FTW_fFindFirst, ref slot, out frame);
                if (hr != 0 || frame == null) // 0 = S_OK
                    return null;

                object? docView;
                hr = frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out docView);
                if (hr != 0 || docView == null) // 0 = S_OK
                    return null;

                return docView;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Ждёт, пока в решении появится ожидаемое число проектов (шаблон всегда разворачивает
        /// фиксированное количество проектов).
        /// </summary>
        private static async Task WaitForProjectsAsync(
            DTE2 dte, int expectedCount, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + ProjectWaitTimeout;
            int lastCount = -1;

            while (true)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                int count;
                try
                {
                    count = CountProjects(dte.Solution);
                }
                catch
                {
                    count = 0;
                }

                if (count != lastCount)
                {
                    Report(progress, "Ожидаю создание проектов в решении… (" + count + "/" + expectedCount + ")");
                    lastCount = count;
                }

                if (count >= expectedCount)
                {
                    Report(progress, "Проекты в решении созданы: " + count + ".");
                    return;
                }

                if (DateTime.UtcNow > deadline)
                {
                    Report(progress, "Не дождался всех проектов решения (" + count + "/" + expectedCount +
                        "), продолжаю с тем, что есть.");
                    return;
                }

                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>Рекурсивный подсчёт проектов в решении (с учётом папок решения).</summary>
        private static int CountProjects(EnvDTE.Solution solution)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (solution == null || solution.Projects == null)
                return 0;

            int count = 0;
            foreach (EnvDTE.Project project in solution.Projects)
                count += CountProjects(project);
            return count;
        }

        private static int CountProjects(EnvDTE.Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (project == null)
                return 0;

            if (string.Equals(project.Kind, SolutionFolderKind, StringComparison.OrdinalIgnoreCase))
            {
                int count = 0;
                if (project.ProjectItems != null)
                {
                    foreach (EnvDTE.ProjectItem item in project.ProjectItems)
                    {
                        EnvDTE.Project? sub = null;
                        try { sub = item.SubProject; }
                        catch { sub = null; }
                        if (sub != null)
                            count += CountProjects(sub);
                    }
                }
                return count;
            }

            return 1;
        }

        private static bool MatchesName(string filePath, string name, string baseName, bool hasExtension)
        {
            if (hasExtension)
                return string.Equals(Path.GetFileName(filePath), name, StringComparison.OrdinalIgnoreCase);
            return string.Equals(Path.GetFileNameWithoutExtension(filePath), baseName, StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> SafeEnumerateDirectories(string path, string pattern, SearchOption option = SearchOption.TopDirectoryOnly)
        {
            try { return Directory.EnumerateDirectories(path, pattern, option); }
            catch { return new string[0]; }
        }

        private static IEnumerable<string> SafeEnumerateFiles(string path, string pattern, SearchOption option = SearchOption.TopDirectoryOnly)
        {
            try { return Directory.EnumerateFiles(path, pattern, option); }
            catch { return new string[0]; }
        }
    }
}
