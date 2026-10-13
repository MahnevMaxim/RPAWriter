#nullable disable
using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace RPAWriter.Commands
{
    /// <summary>Команда «Собрать Release (1 клик)».</summary>
    internal sealed class BuildReleaseCommand
    {
        public const int CommandId = 0x0100;
        public static readonly Guid CommandSet = new Guid("8f4b6a5e-2c1f-4f3a-9d0e-7a6b5c4d3e2f");

        private readonly AsyncPackage _package;

        private BuildReleaseCommand(AsyncPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
        }

        public static BuildReleaseCommand Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new BuildReleaseCommand(package);
            var commandId = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(Instance.Execute, commandId);
            commandService?.AddCommand(menuItem);
        }

        private void Execute(object sender, EventArgs e)
        {
#pragma warning disable VSSDK007 // намеренный fire-and-forget для обработчика команды
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(ExecuteCoreAsync);
#pragma warning restore VSSDK007
        }

        private async Task ExecuteCoreAsync()
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var dte = await _package.GetServiceAsync(typeof(SDTE)) as DTE2;
                dte?.Solution?.SolutionBuild?.SolutionConfigurations?.Item("Release")?.Activate();
                dte?.Solution?.SolutionBuild?.Build(true);
            }
            catch (Exception ex)
            {
                await RPAWriterPackage.LogAsync("Ошибка сборки Release: " + ex.Message);
            }
        }
    }
}
