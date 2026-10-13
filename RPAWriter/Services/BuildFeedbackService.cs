#nullable disable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter.Services
{
    /// <summary>
    /// Обратная связь по сборке: 1-клик Release, чтение ошибок компиляции и окна вывода VS.
    /// Полное чтение ошибок (IErrorList) и окна вывода (IVsOutputWindow) подключается
    /// на этапе интеграции с VS-сервисами.
    /// </summary>
    public class BuildFeedbackService : IBuildFeedbackService
    {
        private readonly AsyncPackage _package;

        public BuildFeedbackService(AsyncPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
        }

        public async Task BuildReleaseAsync(CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var dte = await _package.GetServiceAsync(typeof(SDTE)) as EnvDTE80.DTE2;
            dte?.Solution?.SolutionBuild?.SolutionConfigurations?.Item("Release")?.Activate();
            dte?.Solution?.SolutionBuild?.Build(true);
        }

        public Task<IReadOnlyList<BuildError>> GetCompilationErrorsAsync(CancellationToken cancellationToken = default)
        {
            // TODO: IErrorList / SVsErrorList — подключить на этапе интеграции VS.
            return Task.FromResult<IReadOnlyList<BuildError>>(new List<BuildError>());
        }

        public Task<string> GetOutputWindowTextAsync(CancellationToken cancellationToken = default)
        {
            // TODO: IVsOutputWindow / SVsOutputWindow — подключить на этапе интеграции VS.
            return Task.FromResult(string.Empty);
        }
    }
}
