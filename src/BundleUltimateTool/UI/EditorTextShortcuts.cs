using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BundleUltimateTool.UI
{
    internal static class EditorTextShortcuts
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        internal static Control FocusedEditor()
        {
            IntPtr handle = GetFocus();
            return handle == IntPtr.Zero ? null : Control.FromChildHandle(handle);
        }

        internal static bool TryHandle(Control editor, Keys keyData)
        {
            bool selectAll = keyData == (Keys.Control | Keys.A);
            bool undo = keyData == (Keys.Control | Keys.Z);
            bool redo = keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z);
            if (!selectAll && !undo && !redo) return false;

            if (editor is TextBoxBase textBox)
            {
                if (!textBox.ShortcutsEnabled) return true;
                if (selectAll) textBox.SelectAll();
                else if (!textBox.ReadOnly)
                {
                    if (undo && textBox.CanUndo) textBox.Undo();
                    else if (redo && textBox is RichTextBox richText && richText.CanRedo) richText.Redo();
                }
                // Native text controls already had the first chance to process the key.
                // Consume the fallback even with an empty text undo buffer: a menu shortcut
                // must not undo the document or add files while the user edits text.
                return true;
            }
            if (editor is ComboBox combo && combo.DropDownStyle != ComboBoxStyle.DropDownList)
            {
                if (selectAll) combo.SelectAll();
                // Native combo edit handles its own undo before forwarding to the form.
                return true;
            }
            return false;
        }
    }
}
