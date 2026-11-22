using System;
using System.Collections.Generic;

namespace BundleUltimateTool.Commands
{
    // UI-agnostic command contract: avoids any WPF/WindowsBase dependency.
    internal interface IEditorCommand
    {
        event EventHandler CanExecuteChanged;
        bool CanExecute(object parameter);
        void Execute(object parameter);
    }

    internal enum EditorDocumentCommandId
    {
        Open,
        Exit,
        ToggleTheme,
        LanguageRussian,
        LanguageEnglish,
        ExportRawSelected,
        ExportRawAll,
        ExportTextureSelected,
        ExportTextureAll,
        DdsToNfs,
        ExportObjSelected,
        ExportObjAll,
        SaveStrings,
        ViewDefs,
        AssembleBundle
    }

    // Command set with document-dependent Enable rules for export and table actions.
    internal sealed class EditorDocumentCommands
    {
        private readonly Dictionary<EditorDocumentCommandId, EditorCommand> _commands = new();

        internal EditorDocumentCommands(IReadOnlyDictionary<EditorDocumentCommandId, Action> actions,
            Func<EditorDocumentCommandId, bool> canExecute = null)
        {
            ArgumentNullException.ThrowIfNull(actions);
            foreach (EditorDocumentCommandId id in Enum.GetValues<EditorDocumentCommandId>())
            {
                if (!actions.TryGetValue(id, out Action action) || action == null)
                    throw new ArgumentException("Missing command action: " + id, nameof(actions));
                Func<bool> allowed = canExecute == null ? () => true : () => canExecute(id);
                _commands.Add(id, new EditorCommand(action, allowed));
            }
        }

        internal IEditorCommand this[EditorDocumentCommandId id] => _commands[id];

        internal void Refresh()
        {
            foreach (EditorCommand command in _commands.Values) command.Refresh();
        }

        private sealed class EditorCommand : IEditorCommand
        {
            private readonly Action _execute;
            private readonly Func<bool> _canExecute;
            internal EditorCommand(Action execute, Func<bool> canExecute)
            {
                _execute = execute;
                _canExecute = canExecute;
            }

            public event EventHandler CanExecuteChanged;
            public bool CanExecute(object parameter) => _canExecute();
            public void Execute(object parameter)
            {
                if (CanExecute(parameter)) _execute();
            }
            internal void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
