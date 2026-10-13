#nullable disable
using System;
using System.ComponentModel.Design;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using RPAWriter.Core.Services;

namespace RPAWriter.Commands
{
    /// <summary>Команда «Конвертировать UiPath → Workflow Foundation».</summary>
    internal sealed class ConvertUiPathCommand
    {
        public const int CommandId = 0x0101;
        public static readonly Guid CommandSet = new Guid("8f4b6a5e-2c1f-4f3a-9d0e-7a6b5c4d3e2f");

        private ConvertUiPathCommand() { }

        public static ConvertUiPathCommand Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new ConvertUiPathCommand();
            var menuItem = new MenuCommand(Instance.Execute, new CommandID(CommandSet, CommandId));
            commandService?.AddCommand(menuItem);
        }

        private void Execute(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "Выберите робота UiPath (.xaml)",
                Filter = "UiPath workflow (*.xaml)|*.xaml|Все файлы (*.*)|*.*"
            })
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;

                try
                {
                    var xaml = File.ReadAllText(dialog.FileName);
                    var converter = new UiPathToWfConverter(new RobotGenerator());
                    var result = converter.Convert(xaml);

                    var dir = Path.GetDirectoryName(dialog.FileName);
                    var baseName = Path.GetFileNameWithoutExtension(dialog.FileName);
                    var xamlPath = Path.Combine(dir, baseName + ".wf.xaml");
                    var csPath = Path.Combine(dir, baseName + ".wf.cs");
                    File.WriteAllText(xamlPath, result.Xaml);
                    File.WriteAllText(csPath, result.CSharpCode);

                    MessageBox.Show(
                        "Создано:\n" + xamlPath + "\n" + csPath + "\n\nПредупреждений: " + result.Warnings.Count,
                        "RPAWriter: конвертация UiPath → WF",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка конвертации: " + ex.Message, "RPAWriter",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
