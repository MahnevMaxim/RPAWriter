using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
    /// Создание решения робота из установленного шаблона решения «RPA-XXXX».
    /// Имя решения = «RPA-&lt;номер&gt;». Работает только когда не открыто ни одного решения.
    /// </summary>
    public class RobotTemplateService
    {
        public const string TemplateName = "RPA-XXXX";

        public async Task<RobotCreateResult> CreateRobotAsync(
            string solutionName, string rootPath, CancellationToken cancellationToken = default)
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

            var templatePath = FindTemplate(TemplateName);
            if (templatePath == null)
                return Fail("Шаблон решения \"" + TemplateName + "\" не найден. Установите шаблон и повторите.");

            try
            {
                Directory.CreateDirectory(destDir);
                solution.Create(destDir, solutionName);
                solution.AddFromTemplate(templatePath, destDir, solutionName, false);
                solution.SaveAs(slnFile);
                return new RobotCreateResult { Success = true, Message = "Решение создано.", SolutionPath = slnFile };
            }
            catch (Exception ex)
            {
                return Fail("Не удалось создать решение из шаблона: " + ex.Message);
            }
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

        private static string? FindTemplate(string name)
        {
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

            foreach (var root in roots)
            {
                // Каталог с именем шаблона, внутри которого лежит <name>.vstemplate
                foreach (var dir in SafeEnumerateDirectories(root, name, SearchOption.AllDirectories))
                {
                    var vst = Path.Combine(dir, name + ".vstemplate");
                    if (File.Exists(vst)) return vst;
                }
                // Плоский файл <name>.vstemplate или <name>.zip
                foreach (var f in SafeEnumerateFiles(root, name + ".vstemplate", SearchOption.AllDirectories))
                    return f;
                foreach (var f in SafeEnumerateFiles(root, name + ".zip", SearchOption.AllDirectories))
                    if (string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase))
                        return f;
            }
            return null;
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
