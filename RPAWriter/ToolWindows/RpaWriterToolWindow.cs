#nullable disable
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace RPAWriter.ToolWindows
{
    /// <summary>Окно расширения RPAWriter.</summary>
    [Guid("9d2c5f3b-1e4a-4b7c-9a6d-3f2e1d0c9b8a")]
    public class RpaWriterToolWindow : ToolWindowPane
    {
        public RpaWriterControl Control { get; }

        public RpaWriterToolWindow() : base(null)
        {
            Caption = "RPA Writer";
            Control = new RpaWriterControl();
            Content = Control;
        }
    }
}
