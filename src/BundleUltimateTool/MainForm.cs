using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using BundleUltimateTool.Commands;
using BundleUltimateTool.Documents;
using BundleUltimateTool.Settings;
using BundleUltimateTool.UI;

namespace BundleUltimateTool
{
    // Main editor window: bundle tree, property grid, 2D texture preview and commands.
    public partial class MainForm : Form
    {
        private MenuStrip menuStrip;
        private ToolStripMenuItem fileMenuItem, openMenuItem, exitMenuItem;
        private ToolStripMenuItem exportRawSelectedMenuItem, exportRawAllMenuItem;
        private ToolStripMenuItem exportTextureSelectedMenuItem, exportTextureAllMenuItem;
        private ToolStripMenuItem ddsToNfsMenuItem;
        private ToolStripMenuItem exportObjSelectedMenuItem, exportObjAllMenuItem;
        private ToolStripMenuItem viewMenuItem, themeMenuItem, languageMenuItem;
        private ToolStripMenuItem languageRuItem, languageEnItem;
        private ToolStripMenuItem toolsMenuItem, toolsExtractItem, toolsAssembleItem;
        private ToolStripMenuItem helpMenuItem, aboutMenuItem;

        private ToolStrip mainToolStrip;
        private ToolStripButton openButton;
        private ToolStripButton exportRawButton;
        private ContextMenuStrip treeContextMenu;
        private ToolStripMenuItem exportRawSelectedContextItem;
        private ToolStripMenuItem exportRawAllContextItem;
        private ToolStripMenuItem exportTextureSelectedContextItem;
        private ToolStripMenuItem exportTextureAllContextItem;
        private ToolStripMenuItem exportObjSelectedContextItem;
        private ToolStripMenuItem exportObjAllContextItem;
        private ToolStripMenuItem saveStringsContextItem;
        private ToolStripMenuItem viewDefsContextItem;
        private Label previewMessageLabel;
        private Panel defsPanel;
        private Label defsNameLabel;
        private DataGridView defsGrid;
        private ToolStrip filterToolStrip;
        private ToolStripTextBox searchTextBox;
        private ToolStripComboBox typeFilterComboBox;

        private SplitContainer mainSplit;
        private SplitContainer rightSplit;
        private MultiSelectTreeView nodeTreeView;

        private ToolStrip viewModeToolStrip;
        private ToolStripButton mode2DButton;
        private PictureBox previewBox;
        private PropertyGrid propGrid;

        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusStateLabel;
        private ToolStripStatusLabel statusPathLabel;

        private EditorDocumentCommands _commands;
        private readonly EditorSettingsStore _settingsStore = new();
        private readonly RecentFileHistory _recentFiles = new();

        private bool _isDarkTheme = true;
        private string _uiLanguage = "en";
        private bool _applyingUiLocalization;
        private string _currentFilePath;
        private BundleDocument _document;

        public MainForm(string startupFile = null)
        {
            Name = "app.main";
            Text = "Bundle Ultimate Tool";
            Size = new Size(1280, 760);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;

            UiLocalization.Initialize(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages"));
            LoadSettings();

            InitializeComponentShell();
            InitializeBundleUi();
            InitializeCommands();
            ApplyCommandChrome();
            ApplyTheme();
            ApplyLocalizationToUi();
            UpdateWorkspaceState();

            if (!string.IsNullOrWhiteSpace(startupFile)) OpenBundle(startupFile);
            else SetStatus(UiLocalization.TranslateId("status.ready", "Ready", _uiLanguage));
        }

        private void InitializeComponentShell()
        {
            var renderer = new EditorToolStripRenderer(() => _isDarkTheme);

            // ---- MenuStrip ----
            menuStrip = new MenuStrip { Name = "toolbar.menu", Renderer = renderer };

            fileMenuItem = new ToolStripMenuItem("File") { Name = "menu/file" };
            openMenuItem = new ToolStripMenuItem("Open...") { Name = "menu/file/open", ShortcutKeys = Keys.Control | Keys.O };
            exitMenuItem = new ToolStripMenuItem("Exit") { Name = "menu/file/exit" };
            exportRawSelectedMenuItem = new ToolStripMenuItem("Export raw (selected)...") { Name = "cmd.file.exportRawSelected" };
            exportRawAllMenuItem = new ToolStripMenuItem("Export raw (all)...") { Name = "cmd.file.exportRawAll" };
            exportTextureSelectedMenuItem = new ToolStripMenuItem("Export DDS (selected)...") { Name = "cmd.file.exportTextureSelected" };
            exportTextureAllMenuItem = new ToolStripMenuItem("Export DDS (all)...") { Name = "cmd.file.exportTextureAll" };
            exportObjSelectedMenuItem = new ToolStripMenuItem("Export OBJ (selected)...") { Name = "cmd.file.exportObjSelected" };
            exportObjAllMenuItem = new ToolStripMenuItem("Export OBJ (all)...") { Name = "cmd.file.exportObjAll" };
            fileMenuItem.DropDownItems.AddRange(new ToolStripItem[]
            {
                openMenuItem, new ToolStripSeparator(),
                exportRawSelectedMenuItem, exportRawAllMenuItem,
                new ToolStripSeparator(),
                exportTextureSelectedMenuItem, exportTextureAllMenuItem,
                new ToolStripSeparator(),
                exportObjSelectedMenuItem, exportObjAllMenuItem,
                new ToolStripSeparator(), exitMenuItem
            });

            viewMenuItem = new ToolStripMenuItem("View") { Name = "menu/view" };
            themeMenuItem = new ToolStripMenuItem("Dark theme") { Name = "cmd.view.theme", CheckOnClick = true, Checked = _isDarkTheme };
            languageMenuItem = new ToolStripMenuItem("Language") { Name = "cmd.view.language" };
            languageRuItem = new ToolStripMenuItem("Русский") { Name = "language.option.ru", CheckOnClick = true };
            languageEnItem = new ToolStripMenuItem("English") { Name = "language.option.en", CheckOnClick = true };
            languageMenuItem.DropDownItems.AddRange(new ToolStripItem[] { languageRuItem, languageEnItem });
            viewMenuItem.DropDownItems.AddRange(new ToolStripItem[] { themeMenuItem, languageMenuItem });

            toolsMenuItem = new ToolStripMenuItem("Tools") { Name = "menu/tools" };
            ddsToNfsMenuItem = new ToolStripMenuItem("DDS to NFS texture...") { Name = "cmd.tools.ddsToNfs" };
            toolsExtractItem = new ToolStripMenuItem("Extract...") { Name = "cmd.tools.extract", Enabled = false };
            toolsAssembleItem = new ToolStripMenuItem("Assemble bundle...") { Name = "cmd.tools.assemble" };
            toolsMenuItem.DropDownItems.AddRange(new ToolStripItem[]
            {
                ddsToNfsMenuItem, new ToolStripSeparator(), toolsExtractItem, toolsAssembleItem
            });

            helpMenuItem = new ToolStripMenuItem("Help") { Name = "menu/help" };
            aboutMenuItem = new ToolStripMenuItem("About...") { Name = "cmd.help.about" };
            helpMenuItem.DropDownItems.Add(aboutMenuItem);

            menuStrip.Items.AddRange(new ToolStripItem[] { fileMenuItem, viewMenuItem, toolsMenuItem, helpMenuItem });

            // ---- Quick-access toolbar ----
            mainToolStrip = new ToolStrip { Name = "toolbar.quickAccess", GripStyle = ToolStripGripStyle.Hidden, Renderer = renderer };
            openButton = new ToolStripButton("Open") { Name = "cmd.file.open", DisplayStyle = ToolStripItemDisplayStyle.ImageAndText };
            exportRawButton = new ToolStripButton("Export raw") { Name = "cmd.file.exportRawSelected", DisplayStyle = ToolStripItemDisplayStyle.ImageAndText };
            mainToolStrip.Items.AddRange(new ToolStripItem[] { openButton, new ToolStripSeparator(), exportRawButton });

            // ---- Dense resource filter row ----
            filterToolStrip = new ToolStrip { Name = "toolbar.resourceBrowser", GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, Renderer = renderer };
            searchTextBox = new ToolStripTextBox { Name = "input.search", Size = new Size(100, 25) };
            typeFilterComboBox = new ToolStripComboBox
            {
                Name = "input.type",
                DropDownStyle = ComboBoxStyle.DropDownList,
                Size = new Size(120, 25),
                Enabled = false
            };
            filterToolStrip.Items.AddRange(new ToolStripItem[]
            {
                new ToolStripLabel("Search") { Name = "label.search" }, searchTextBox,
                new ToolStripSeparator(),
                new ToolStripLabel("Type:") { Name = "label.type" }, typeFilterComboBox
            });

            // ---- Tree ----
            nodeTreeView = new MultiSelectTreeView { Name = "tree.resources", Dock = DockStyle.Fill, HideSelection = false };

            treeContextMenu = new ContextMenuStrip { Name = "context.resources", Renderer = renderer };
            exportRawSelectedContextItem = new ToolStripMenuItem("Export raw (selected)...") { Name = "context.exportRawSelected" };
            exportRawAllContextItem = new ToolStripMenuItem("Export raw (all)...") { Name = "context.exportRawAll" };
            exportTextureSelectedContextItem = new ToolStripMenuItem("Export DDS (selected)...") { Name = "context.exportTextureSelected" };
            exportTextureAllContextItem = new ToolStripMenuItem("Export DDS (all)...") { Name = "context.exportTextureAll" };
            exportObjSelectedContextItem = new ToolStripMenuItem("Export OBJ (selected)...") { Name = "context.exportObjSelected" };
            exportObjAllContextItem = new ToolStripMenuItem("Export OBJ (all)...") { Name = "context.exportObjAll" };
            saveStringsContextItem = new ToolStripMenuItem("Save strings...") { Name = "cmd.context.saveStrings" };
            viewDefsContextItem = new ToolStripMenuItem("View defs table...") { Name = "cmd.context.viewDefs" };
            treeContextMenu.Items.AddRange(new ToolStripItem[]
            {
                exportRawSelectedContextItem, exportRawAllContextItem,
                new ToolStripSeparator(),
                exportTextureSelectedContextItem, exportTextureAllContextItem,
                new ToolStripSeparator(),
                exportObjSelectedContextItem, exportObjAllContextItem,
                new ToolStripSeparator(),
                saveStringsContextItem, viewDefsContextItem
            });
            nodeTreeView.ContextMenuStrip = treeContextMenu;

            var leftPanel = new Panel { Dock = DockStyle.Fill };
            leftPanel.Controls.Add(nodeTreeView);
            leftPanel.Controls.Add(filterToolStrip);

            // ---- Preview side ----
            previewBox = new PictureBox
            {
                Name = "preview.bundle",
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.Fixed3D
            };
            viewModeToolStrip = new ToolStrip
            {
                Name = "toolbar.viewMode", Dock = DockStyle.Top,
                GripStyle = ToolStripGripStyle.Hidden, Renderer = renderer
            };
            mode2DButton = new ToolStripButton("Texture")
            {
                Name = "view.mode2d", Checked = true, CheckOnClick = false,
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold)
            };
            viewModeToolStrip.Items.AddRange(new ToolStripItem[]
            {
                new ToolStripLabel("Preview") { Name = "view.mode.label" },
                new ToolStripSeparator(), mode2DButton
            });

            var topViewPanel = new Panel { Dock = DockStyle.Fill };
            topViewPanel.Controls.Add(previewBox);
            topViewPanel.Controls.Add(viewModeToolStrip);
            previewMessageLabel = new Label
            {
                Name = "preview.message", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(8), Visible = false
            };
            topViewPanel.Controls.Add(previewMessageLabel);

            propGrid = new PropertyGrid
            {
                Name = "properties.resource", Dock = DockStyle.Fill,
                HelpVisible = false, ToolbarVisible = false
            };
            var propertyBodyPanel = new Panel { Dock = DockStyle.Fill };
            propertyBodyPanel.Controls.Add(propGrid);
            var propertyPanel = new Panel { Dock = DockStyle.Fill };
            propertyPanel.Controls.Add(propertyBodyPanel);

            // Defs table view is docked inside the existing property panel: no extra window.
            defsPanel = new Panel { Dock = DockStyle.Fill, Visible = false };
            defsNameLabel = new Label
            {
                Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0), AutoEllipsis = true
            };
            defsGrid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            defsGrid.Columns.Add("fieldId", "Field ID");
            defsGrid.Columns.Add("offset", "Offset");
            defsGrid.Columns.Add("type", "Type");
            defsPanel.Controls.Add(defsGrid);
            defsPanel.Controls.Add(defsNameLabel);
            propertyPanel.Controls.Add(defsPanel);

            rightSplit = new SplitContainer
            {
                Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                SplitterDistance = 420, SplitterWidth = 5
            };
            rightSplit.Panel1.Controls.Add(topViewPanel);
            rightSplit.Panel2.Controls.Add(propertyPanel);

            mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill, Orientation = Orientation.Vertical,
                SplitterDistance = 380, SplitterWidth = 5, FixedPanel = FixedPanel.Panel1
            };
            mainSplit.Panel1.Controls.Add(leftPanel);
            mainSplit.Panel2.Controls.Add(rightSplit);

            mainSplit.Paint += (s, e) => PaintSplitter(s as SplitContainer, e.Graphics);
            rightSplit.Paint += (s, e) => PaintSplitter(s as SplitContainer, e.Graphics);

            // ---- Status strip ----
            statusStrip = new StatusStrip { Name = "status.bar", Renderer = renderer, SizingGrip = false };
            statusStateLabel = new ToolStripStatusLabel("Ready") { Name = "status.state" };
            statusPathLabel = new ToolStripStatusLabel(string.Empty) { Name = "status.path", Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            statusStrip.Items.AddRange(new ToolStripItem[] { statusStateLabel, statusPathLabel });

            Controls.Add(mainSplit);
            Controls.Add(statusStrip);
            Controls.Add(mainToolStrip);
            Controls.Add(menuStrip);

            InitializeWorkspaceStates(topViewPanel, propertyBodyPanel);

            DragEnter += (s, e) =>
            {
                e.Effect = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)
                    ? DragDropEffects.Copy : DragDropEffects.None;
            };
            DragDrop += (s, e) =>
            {
                if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                    OpenBundle(files[0]);
            };
        }

        private void ApplyCommandChrome()
        {
            EditorCommandChrome.Apply(menuStrip, false);
            EditorCommandChrome.Apply(mainToolStrip, true);
            EditorCommandChrome.Apply(filterToolStrip, false);
            EditorCommandChrome.Apply(viewModeToolStrip, false);
            EditorCommandChrome.Apply(treeContextMenu, false);
            DpiChanged += (s, e) =>
            {
                EditorCommandChrome.Apply(menuStrip, false);
                EditorCommandChrome.Apply(treeContextMenu, false);
            };
        }

        private void SetStatus(string state)
        {
            if (statusStateLabel != null && !statusStateLabel.IsDisposed)
                statusStateLabel.Text = state ?? string.Empty;
        }

    }
}
