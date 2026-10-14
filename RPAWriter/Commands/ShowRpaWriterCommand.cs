#nullable disable
using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using RPAWriter.ToolWindows;

namespace RPAWriter.Commands
{
    /// <summary>Команда «Открыть RPA Writer» — показывает единое окно расширения.</summary>
    internal sealed class ShowRpaWriterCommand
    {
        public const int CommandId = 0x0102;
        public static readonly Guid CommandSet = new Guid("8f4b6a5e-2c1f-4f3a-9d0e-7a6b5c4d3e2f");

        private readonly AsyncPackage _package;

        private ShowRpaWriterCommand(AsyncPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
        }

        public static ShowRpaWriterCommand Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new ShowRpaWriterCommand(package);
            commandService?.AddCommand(new MenuCommand(Instance.Execute, new CommandID(CommandSet, CommandId)));
        }

        private void Execute(object sender, EventArgs e)
        {
#pragma warning disable VSSDK007
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(ExecuteCoreAsync);
#pragma warning restore VSSDK007
        }

        private async Task ExecuteCoreAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var window = await _package.FindToolWindowAsync(typeof(RpaWriterToolWindow), 0, true, _package.DisposalToken);
            if (window?.Frame == null) return;

            if (window is RpaWriterToolWindow rpaWindow)
                rpaWindow.Control.Initialize(_package);

            if (window.Frame is IVsWindowFrame frame)
                frame.Show();
        }
    }
}
