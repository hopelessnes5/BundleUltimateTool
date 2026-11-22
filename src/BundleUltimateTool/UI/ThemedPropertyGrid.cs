using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace BundleUltimateTool.UI;

/// <summary>
/// Compact, fully themeable replacement for WinForms PropertyGrid, implemented as a
/// two-column DataGridView. Selection rule: when an array is supplied, the FIRST object
/// is displayed (the shell never selects heterogeneous objects); the array itself is
/// still returned by SelectedObjects so existing callers keep working.
/// </summary>
internal sealed class ThemedPropertyGrid : DataGridView
{
    private object[] _selectedObjects = Array.Empty<object>();
    private EditorPalette _palette = EditorPalette.ForTheme(true);

    public ThemedPropertyGrid()
    {
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        RowHeadersVisible = false;
        ReadOnly = true;
        MultiSelect = false;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        ScrollBars = ScrollBars.Both;
        ColumnHeadersHeight = 22;
        RowTemplate.Height = 20;
        BorderStyle = BorderStyle.None;
        Dock = DockStyle.Fill;
        EnableHeadersVisualStyles = false;

        Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "property", HeaderText = "Property", Width = 160, ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "value", HeaderText = "Value", ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        ApplyPalette(_palette);
    }

    public event EventHandler SelectedObjectsChanged;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object SelectedObject
    {
        get => _selectedObjects.FirstOrDefault();
        set => SetObjects(value == null ? Array.Empty<object>() : new[] { value });
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public object[] SelectedObjects
    {
        get => (object[])_selectedObjects.Clone();
        set => SetObjects(value ?? Array.Empty<object>());
    }

    public void ApplyPalette(EditorPalette palette)
    {
        _palette = palette ?? EditorPalette.ForTheme(true);
        BackgroundColor = _palette.Surface;
        GridColor = _palette.Border;
        DefaultCellStyle.BackColor = _palette.Surface;
        DefaultCellStyle.ForeColor = _palette.Text;
        DefaultCellStyle.SelectionBackColor = _palette.Selection;
        DefaultCellStyle.SelectionForeColor = _palette.SelectionText;
        ColumnHeadersDefaultCellStyle.BackColor = _palette.Toolbar;
        ColumnHeadersDefaultCellStyle.ForeColor = _palette.Text;
        ColumnHeadersDefaultCellStyle.SelectionBackColor = _palette.Toolbar;
        ColumnHeadersDefaultCellStyle.SelectionForeColor = _palette.Text;
        foreach (DataGridViewRow row in Rows)
        {
            if (row.Tag is string tag && tag == "category")
            {
                row.DefaultCellStyle.BackColor = _palette.Toolbar;
                row.DefaultCellStyle.ForeColor = _palette.Text;
                row.DefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
                row.DefaultCellStyle.SelectionBackColor = _palette.Toolbar;
                row.DefaultCellStyle.SelectionForeColor = _palette.Text;
            }
        }
    }

    private void SetObjects(object[] objects)
    {
        bool changed = objects.Length != _selectedObjects.Length ||
                       objects.Where((value, index) => !ReferenceEquals(value, _selectedObjects[index])).Any();
        if (!changed) return;
        _selectedObjects = (object[])objects.Clone();
        BuildRows();
        SelectedObjectsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BuildRows()
    {
        Rows.Clear();
        object target = _selectedObjects.FirstOrDefault();
        foreach (PropertyRow row in PropertyRowBuilder.Build(target))
        {
            int index = Rows.Add(row.Name, row.Value);
            if (row.IsCategory) Rows[index].Tag = "category";
        }
        ApplyPalette(_palette);
        if (Rows.Count > 0) Rows[0].Selected = false;
    }
}
