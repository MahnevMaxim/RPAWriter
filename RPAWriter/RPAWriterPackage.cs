using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using RPAWriter.Commands;
using RPAWriter.Options;

namespace RPAWriter
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(RPAWriterPackage.PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideOptionPage(typeof(RpaWriterOptionsPage), "RPAWriter", "Общие", 0, 0, true)]
    public sealed class RPAWriterPackage : AsyncPackage
    {
        /// <summary>
        /// RPAWriterPackage GUID string.
        /// </summary>
        public const string PackageGuidString = "a34783b7-3df3-4c15-b8b4-39fd051a175f";

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited.
        /// </summary>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // When initialized asynchronously, the current thread may be a background thread at this point.
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            await BuildReleaseCommand.InitializeAsync(this);
            await ConvertUiPathCommand.InitializeAsync(this);
        }

        /// <summary>Простой хелпер для логирования ошибок в ActivityLog.</summary>
        public static async Task LogAsync(string message)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            ActivityLog.LogError("RPAWriter", message);
        }

        #endregion
    }
}
