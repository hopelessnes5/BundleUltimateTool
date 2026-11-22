using System;
using System.Windows.Forms;
using BundleUltimateTool.Commands;

namespace BundleUltimateTool.UI
{
    internal static class ToolStripCommandBinding
    {
        internal static void Bind(ToolStripItem item, IEditorCommand command)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(command);
            EventHandler update = (_, _) => item.Enabled = command.CanExecute(null);
            EventHandler execute = (_, _) => command.Execute(null);
            EventHandler dispose = null;
            dispose = (_, _) =>
            {
                command.CanExecuteChanged -= update;
                item.Click -= execute;
                item.Disposed -= dispose;
            };
            command.CanExecuteChanged += update;
            item.Click += execute;
            item.Disposed += dispose;
            update(null, EventArgs.Empty);
        }
    }
}
