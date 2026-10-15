using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using NuGet.VisualStudio;

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

            // После создания решения переустанавливаем пакеты NuGet.
            var message = "Решение создано.";
            try
            {
                ReinstallPackages(dte, progress);
            }
            catch (Exception ex)
            {
                message += " Не удалось переустановить пакеты NuGet: " + ex.Message;
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
        /// Переустанавливает (reinstall) пакеты NuGet во всех проектах решения:
        /// для каждого установленного пакета — сначала удаление, затем установка той же версии.
        /// Использует документированные MEF-сервисы NuGet.VisualStudio (IVsPackageInstaller и др.).
        /// </summary>
        private static void ReinstallPackages(DTE2 dte, IProgress<string>? progress)
        {
            var componentModel = Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel;
            if (componentModel == null)
                throw new InvalidOperationException("Не удалось получить компонентную модель VS.");

            var services = componentModel.GetService<IVsPackageInstallerServices>();
            var uninstaller = componentModel.GetService<IVsPackageUninstaller>();
            var installer = componentModel.GetService<IVsPackageInstaller>();

            foreach (Project project in EnumerateProjects(dte))
            {
#pragma warning disable CS0618 // GetInstalledPackages(Project) устарел в пользу INuGetProjectService
                var installed = services.GetInstalledPackages(project).ToList();
#pragma warning restore CS0618

                if (installed.Count == 0)
                    continue;

                Report(progress, "Переустанавливаю пакеты проекта: " + project.Name);
                foreach (var pkg in installed)
                {
                    Report(progress, "  Reinstall: " + pkg.Id + " " + pkg.VersionString);
                    uninstaller.UninstallPackage(project, pkg.Id, removeDependencies: false);
                    installer.InstallPackage(null, project, pkg.Id, pkg.VersionString, ignoreDependencies: false);
                }
            }
        }

        private static IEnumerable<Project> EnumerateProjects(DTE2 dte)
        {
            var solution = dte?.Solution;
            if (solution == null)
                yield break;

            foreach (Project project in solution.Projects)
                foreach (var inner in EnumerateProjectTree(project))
                    yield return inner;
        }

        private static IEnumerable<Project> EnumerateProjectTree(Project project)
        {
            if (project == null)
                yield break;

            // Папку решения (solution folder) разворачиваем рекурсивно.
            if (project.Kind == EnvDTE80.ProjectKinds.vsProjectKindSolutionFolder)
            {
                foreach (ProjectItem item in project.ProjectItems)
                    if (item.SubProject is Project sub)
                        foreach (var inner in EnumerateProjectTree(sub))
                            yield return inner;
                yield break;
            }

            yield return project;
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
