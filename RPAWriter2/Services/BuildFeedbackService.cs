using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using RPAWriter.Core.Contracts;
using RPAWriter.Core.Models;

namespace RPAWriter2.Services
{
    /// <summary>
    /// Обратная связь по сборке: 1-клик Release.
    /// Чтение ошибок компиляции (IErrorList) и окна вывода (IVsOutputWindow) —
    /// заглушки, подключаются позже.
    /// </summary>
    public class BuildFeedbackService : IBuildFeedbackService
    {
        public async Task BuildReleaseAsync(CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            if (dte?.Solution == null || !dte.Solution.IsOpen)
                throw new InvalidOperationException("Нет открытого решения.");
            dte.Solution.SolutionBuild?.SolutionConfigurations?.Item("Release")?.Activate();
            dte.Solution.SolutionBuild?.Build(true);
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
