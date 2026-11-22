using System;
using System.Drawing;
using System.Windows.Forms;

namespace BundleUltimateTool.UI
{
    internal sealed class EditorEmptyStatePanel : TableLayoutPanel
    {
        private readonly Label _title;
        private readonly Label _description;
        private readonly Button _open;
        private readonly Font _headingFont = new Font("Segoe UI", 12F, FontStyle.Bold);
        private readonly Font _bodyFont = new Font("Segoe UI", 9F);

        internal EditorEmptyStatePanel(Action open)
        {
            Name = "workspace.empty";
            Dock = DockStyle.Fill;
            ColumnCount = 1; RowCount = 3;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            var content = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.None, ColumnCount = 1, RowCount = 3,
                Margin = new Padding(8), Padding = Padding.Empty
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _title = new Label
            {
                Name = "workspace.empty.title", AutoSize = true, Font = _headingFont,
                MaximumSize = new Size(420, 0), TextAlign = ContentAlignment.MiddleCenter,
                Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 0, 6)
            };
            _description = new Label
            {
                Name = "workspace.empty.description", AutoSize = true, Font = _bodyFont,
                MaximumSize = new Size(420, 0), TextAlign = ContentAlignment.MiddleCenter,
                Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 0, 10)
            };
            _open = new Button
            {
                Name = "workspace.empty.open", AutoSize = true, Font = _bodyFont,
                Anchor = AnchorStyles.None, Padding = new Padding(8, 2, 8, 2),
                FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false
            };
            _open.Click += (_, _) => open();
            content.Controls.Add(_title, 0, 0);
            content.Controls.Add(_description, 0, 1);
            content.Controls.Add(_open, 0, 2);
            Controls.Add(content, 0, 1);
            SizeChanged += (_, _) =>
            {
                int width = Math.Min((int)Math.Round(420 * DeviceDpi / 96d), Math.Max(100, ClientSize.Width - 24));
                _title.MaximumSize = new Size(width, 0);
                _description.MaximumSize = new Size(width, 0);
            };
        }

        internal void SetText(string title, string description, string openLabel)
        {
            _title.Text = title;
            _description.Text = description;
            _open.Text = openLabel;
            _open.AccessibleName = openLabel;
        }

        internal void ApplyPalette(EditorPalette palette)
        {
            BackColor = palette.Surface;
            ForeColor = palette.Text;
            _title.ForeColor = palette.Text;
            _description.ForeColor = palette.SecondaryText;
            _open.BackColor = palette.Input;
            _open.ForeColor = palette.Text;
            _open.FlatAppearance.BorderColor = palette.Accent;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) { _headingFont.Dispose(); _bodyFont.Dispose(); }
        }
    }
}
