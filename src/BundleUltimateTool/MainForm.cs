using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using BundleUltimateTool.Commands;
using BundleUltimateTool.Documents;
using BundleUltimateTool.Settings;
using BundleUltimateTool.Rendering;
using BundleUltimateTool.UI;

namespace BundleUltimateTool
{
    // Main editor window: bundle tree, property grid, 2D texture preview and commands.
    public partial class MainForm : Form
    {
        private MenuStrip menuStrip;
        private ToolStripMenuItem fileMenuItem, openMenuItem, exitMenuItem, selectGameFolderMenuItem;
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
        private ToolStripComboBox sortComboBox;
        private ToolStripButton archiveModeButton;
        private ToolStripButton filesModeButton;
        private ToolStripButton selectGameFolderButton;
        private ToolStripButton folderUpButton;
        private ToolStripLabel breadcrumbLabel;

        private SplitContainer mainSplit;
        private SplitContainer rightSplit;
        private MultiSelectTreeView nodeTreeView;
        private MultiSelectTreeView gameFileTreeView;
        private ContextMenuStrip gameFileContextMenu;
        private ToolStripMenuItem openFromFolderItem;
        private ToolStripMenuItem copyPathItem;

        private ToolStrip viewModeToolStrip;
        private ToolStripButton mode2DButton;
        private ToolStripButton mode3DButton;
        private ToolStrip channelToolStrip;
        private ToolStripButton channelRButton;
        private ToolStripButton channelGButton;
        private ToolStripButton channelBButton;
        private ToolStripButton channelAButton;
        private ToolStripButton textureModeRgbButton;
        private ToolStripButton textureModeRgbaButton;
        private Panel previewScrollPanel;
        private PictureBox previewBox;
        private Model3DView model3DView;
        private TextBox filePreviewTextBox;
        private ThemedPropertyGrid propGrid;

        private StatusStrip statusStrip;
        private ToolStripStatusLabel statusStateLabel;
        private ToolStripStatusLabel statusPathLabel;
        private ToolStripProgressBar statusProgress;
        private TableLayoutPanel noResultsPanel;
        private TableLayoutPanel noResultsContent;
        private Label noResultsLabel;
        private Button clearFilterButton;

        private EditorDocumentCommands _commands;
        private readonly EditorSettingsStore _settingsStore = new();
        private readonly RecentFileHistory _recentFiles = new();

        private bool _isDarkTheme = true;
        private string _uiLanguage = "en";
        private bool _applyingUiLocalization;
        private string _currentFilePath;
        private string _gameFolderPath;
        private string _startupFile;
        private string _startupGameFolder;
        private bool _startupSwitchToFiles;
        private int _startupSelectedEntry = -1;
        private bool _startupHandled;
        private BundleDocument _document;
        private readonly List<BundleDocument> _openDocuments = new();
        private readonly BundleReaderCache _readerCache = new(4);
        private bool _compositeMode;

        private sealed class ShadingOption
        {
            internal ShadingOption(Dx12ShadingMode mode, string text) { Mode = mode; Text = text; }
            internal Dx12ShadingMode Mode { get; }
            internal string Text { get; }
            public override string ToString() => Text;
        }
        private readonly GameFolderModel _gameFolderModel = new();
        private bool _filesMode;

        public MainForm(string startupFile = null, string gameFolderPath = null, int selectedEntry = -1)
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

            if (!string.IsNullOrWhiteSpace(gameFolderPath))
            { _startupGameFolder = gameFolderPath; _startupSwitchToFiles = true; }
            else if (!string.IsNullOrWhiteSpace(_gameFolderPath))
            { _startupGameFolder = _gameFolderPath; _startupSwitchToFiles = false; }
            _startupFile = startupFile;
            _startupSelectedEntry = selectedEntry;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // SplitterDistance set in the initializer is clamped before layout; apply the
            // one-third split once the actual client size is known. Never reset it later
            // so a user-dragged divider is preserved.
            if (mainSplit != null)
            {
                const int leftMinimum = 300;
                const int rightMinimum = 520;
                int maximum = Math.Max(leftMinimum, ClientSize.Width - rightMinimum);
                int desired = Math.Clamp(ClientSize.Width / 3, leftMinimum, maximum);
                // Set a valid distance before the min sizes, otherwise each min-size
                // setter validates against the old (too small) default distance and throws.
                mainSplit.SplitterDistance = desired;
                mainSplit.Panel1MinSize = leftMinimum;
                mainSplit.Panel2MinSize = rightMinimum;
            }
            ApplyTheme();
        }

        private void InitializeComponentShell()
        {
            var renderer = new EditorToolStripRenderer(() => _isDarkTheme);

            // ---- MenuStrip ----
            menuStrip = new MenuStrip { Name = "toolbar.menu", Renderer = renderer };

            fileMenuItem = new ToolStripMenuItem("File") { Name = "menu/file" };
            openMenuItem = new ToolStripMenuItem("Open...") { Name = "menu/file/open", ShortcutKeys = Keys.Control | Keys.O };
            selectGameFolderMenuItem = new ToolStripMenuItem("Select game folder...") { Name = "cmd.file.selectGameFolder" };
            exitMenuItem = new ToolStripMenuItem("Exit") { Name = "menu/file/exit" };
            exportRawSelectedMenuItem = new ToolStripMenuItem("Export raw (selected)...") { Name = "cmd.file.exportRawSelected" };
            exportRawAllMenuItem = new ToolStripMenuItem("Export raw (all)...") { Name = "cmd.file.exportRawAll" };
            exportTextureSelectedMenuItem = new ToolStripMenuItem("Export DDS (selected)...") { Name = "cmd.file.exportTextureSelected" };
            exportTextureAllMenuItem = new ToolStripMenuItem("Export DDS (all)...") { Name = "cmd.file.exportTextureAll" };
            exportObjSelectedMenuItem = new ToolStripMenuItem("Export OBJ (selected)...") { Name = "cmd.file.exportObjSelected" };
            exportObjAllMenuItem = new ToolStripMenuItem("Export OBJ (all)...") { Name = "cmd.file.exportObjAll" };
            fileMenuItem.DropDownItems.AddRange(new ToolStripItem[]
            {
                openMenuItem, selectGameFolderMenuItem, new ToolStripSeparator(),
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
            openButton = new ToolStripButton("Open")
            {
                Name = "cmd.file.open", DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                ToolTipText = UiLocalization.TranslateId("cmd.file.open.tooltip", "Open a bundle (Ctrl+O)", _uiLanguage)
            };
            exportRawButton = new ToolStripButton("Export raw")
            {
                Name = "cmd.file.exportRawSelected", DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                ToolTipText = UiLocalization.TranslateId("cmd.file.exportRawSelected.tooltip",
                    "Extract raw .nfs for selected entries", _uiLanguage)
            };
            selectGameFolderButton = new ToolStripButton("Game folder")
            {
                Name = "cmd.file.selectGameFolder", DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                ToolTipText = UiLocalization.TranslateId("cmd.file.selectGameFolder.tooltip",
                    "Select the game installation folder", _uiLanguage)
            };
            folderUpButton = new ToolStripButton("Up")
            {
                Name = "cmd.folder.up", DisplayStyle = ToolStripItemDisplayStyle.Image,
                Enabled = false,
                ToolTipText = UiLocalization.TranslateId("cmd.folder.up.tooltip", "Go up one folder", _uiLanguage)
            };
            breadcrumbLabel = new ToolStripLabel(string.Empty)
            {
                Name = "folder.breadcrumb", Visible = false, Overflow = ToolStripItemOverflow.Never
            };
            // Up button removed: folder navigation is done by clicking the folder node.
            mainToolStrip.Items.AddRange(new ToolStripItem[]
            {
                openButton, selectGameFolderButton, new ToolStripSeparator(),
                breadcrumbLabel, new ToolStripSeparator(), exportRawButton
            });

            // ---- Dense resource filter row (copied layout pattern from PrototypeUltimateTool:MainForm.cs) ----
            filterToolStrip = new ToolStrip { Name = "toolbar.resourceBrowser", GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, Renderer = renderer };
            searchTextBox = new ToolStripTextBox { Name = "input.search", Size = new Size(80, 25) };
            typeFilterComboBox = new ToolStripComboBox
            {
                Name = "input.type",
                DropDownStyle = ComboBoxStyle.DropDownList,
                Size = new Size(118, 25),
                AutoSize = false,
                Enabled = false
            };
            typeFilterComboBox.ComboBox.DrawMode = DrawMode.OwnerDrawFixed;
            typeFilterComboBox.ComboBox.DrawItem += DrawThemedComboBoxItem;
            archiveModeButton = new ToolStripButton("Archive")
            {
                Name = "cmd.view.archive", CheckOnClick = true, Checked = true,
                ToolTipText = UiLocalization.TranslateId("cmd.view.archive.tooltip", "Browse the open archive", _uiLanguage)
            };
            filesModeButton = new ToolStripButton("Files")
            {
                Name = "cmd.view.files", CheckOnClick = true, Checked = false,
                ToolTipText = UiLocalization.TranslateId("cmd.view.files.tooltip", "Browse the game folder", _uiLanguage)
            };
            archiveModeButton.CheckedChanged += (s, e) =>
            {
                if (archiveModeButton.Checked && _filesMode) SetFilesMode(false);
            };
            filesModeButton.CheckedChanged += (s, e) =>
            {
                if (filesModeButton.Checked && !_filesMode) SetFilesMode(true);
            };
            sortComboBox = new ToolStripComboBox
            {
                Name = "input.sort", DropDownStyle = ComboBoxStyle.DropDownList, Size = new Size(120, 25)
            };
            // Copied from PrototypeUltimateTool:MainForm.cs (sort dropdown, lines 443-455).
            sortComboBox.Items.AddRange(new object[]
            {
                "По умолчанию",
                "По имени (А → Я)",
                "По имени (Я → А)",
                "По размеру (убыв.)",
                "По размеру (возр.)",
                "По разрешению (убыв.)",
                "По разрешению (возр.)",
                "По типу"
            });
            sortComboBox.SelectedIndex = 0;
            sortComboBox.ComboBox.DrawMode = DrawMode.OwnerDrawFixed;
            sortComboBox.ComboBox.DrawItem += DrawThemedComboBoxItem;
            sortComboBox.SelectedIndexChanged += (s, e) =>
            {
                ApplicationLog.Write("SORT_CHANGED", $"mode={sortComboBox.SelectedIndex}, filesMode={_filesMode}");
                if (_applyingUiLocalization) return;
                if (_filesMode) RefreshGameTree(); else RebuildTree();
            };
            // Search field and Archive toggle removed as useless/space-consuming.
            // The controls remain hidden so existing handlers stay valid.
            searchTextBox.Visible = false;
            archiveModeButton.Visible = false;
            filterToolStrip.Items.AddRange(new ToolStripItem[]
            {
                filesModeButton, new ToolStripSeparator(),
                new ToolStripLabel("Type:") { Name = "label.type" }, typeFilterComboBox,
                new ToolStripSeparator(),
                new ToolStripLabel("Sort:") { Name = "label.sort" }, sortComboBox
            });

            // ---- Tree (layout pattern copied from PrototypeUltimateTool:MainForm.cs, lines 509-526) ----
            nodeTreeView = new MultiSelectTreeView
            {
                Name = "tree.resources",
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                HideSelection = false,
                FullRowSelect = true,
                HotTracking = true,
                ShowNodeToolTips = true,
                ShowLines = true,
                ShowRootLines = true,
                Indent = 18,
                ItemHeight = 22,
                Font = new Font("Segoe UI", 9.0f, FontStyle.Regular, GraphicsUnit.Point),
                ImageList = TreeIcons.Create()
            };

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

            gameFileTreeView = new MultiSelectTreeView
            {
                Name = "tree.gameFiles",
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                HideSelection = false,
                FullRowSelect = true,
                HotTracking = true,
                ShowNodeToolTips = true,
                ShowLines = true,
                ShowRootLines = true,
                Indent = 18,
                ItemHeight = 22,
                Font = new Font("Segoe UI", 9.0f, FontStyle.Regular, GraphicsUnit.Point),
                Visible = false,
                ImageList = TreeIcons.Create()
            };
            gameFileContextMenu = new ContextMenuStrip { Name = "context.gameFiles", Renderer = renderer };
            openFromFolderItem = new ToolStripMenuItem("Open as archive") { Name = "cmd.file.openFromFolder" };
            copyPathItem = new ToolStripMenuItem("Copy full path") { Name = "cmd.file.copyPath" };
            gameFileContextMenu.Items.AddRange(new ToolStripItem[] { openFromFolderItem, copyPathItem });
            gameFileTreeView.ContextMenuStrip = gameFileContextMenu;

            var leftPanel = new Panel { Dock = DockStyle.Fill };
            leftPanel.Controls.Add(nodeTreeView);
            leftPanel.Controls.Add(gameFileTreeView);
            leftPanel.Controls.Add(filterToolStrip);

            // No-results state lives inside the tree panel, never as a new window.
            noResultsPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Visible = false
            };
            noResultsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            noResultsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            noResultsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            noResultsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            noResultsContent = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.None, ColumnCount = 1, RowCount = 2,
                Margin = new Padding(4), Padding = Padding.Empty
            };
            noResultsContent.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            noResultsContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            noResultsContent.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            noResultsLabel = new Label
            {
                AutoSize = true, MaximumSize = new Size(260, 0), TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0, 0, 0, 4)
            };
            clearFilterButton = new Button
            {
                AutoSize = true, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
                Padding = new Padding(8, 2, 8, 2), Margin = Padding.Empty
            };
            clearFilterButton.Click += (s, e) => ClearFilters();
            noResultsContent.Controls.Add(noResultsLabel, 0, 0);
            noResultsContent.Controls.Add(clearFilterButton, 0, 1);
            noResultsPanel.Controls.Add(noResultsContent, 0, 1);
            leftPanel.Controls.Add(noResultsPanel);

            // ---- Preview side ----
            previewScrollPanel = new Panel
            {
                Name = "viewer.texture2d.panel", Dock = DockStyle.Fill,
                AutoScroll = true, BorderStyle = BorderStyle.Fixed3D
            };
            previewBox = new PictureBox
            {
                Name = "preview.texture2d",
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.Fixed3D
            };
            previewScrollPanel.Controls.Add(previewBox);
            viewModeToolStrip = new ToolStrip
            {
                Name = "toolbar.viewMode", Dock = DockStyle.Top,
                GripStyle = ToolStripGripStyle.Hidden, Renderer = renderer
            };
            mode2DButton = new ToolStripButton("Texture")
            {
                Name = "view.mode2d", Checked = true, CheckOnClick = false,
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
                ToolTipText = UiLocalization.TranslateId("view.mode2d.tooltip", "Texture preview", _uiLanguage)
            };
            mode3DButton = new ToolStripButton("Model 3D")
            {
                Name = "view.mode3d", Checked = false, CheckOnClick = false,
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
                ToolTipText = UiLocalization.TranslateId("view.mode3d.tooltip", "3D model view", _uiLanguage)
            };
            mode2DButton.Click += (s, e) =>
            {
                if (_previewTextureSource != null) { SetPreviewMode(false); RefreshTexturePreview(); }
                else if (model3DView.HasModel) { SetPreviewMode(false); previewMessageLabel.Visible = true; previewMessageLabel.Text = UiLocalization.TranslateId("preview.noTexture", "No texture preview for this entry.", _uiLanguage); }
            };
            mode3DButton.Click += (s, e) =>
            {
                if (model3DView.HasModel) { SetPreviewMode(true); SetModel3DStatus(); }
            };
            // View-mode strip ported from PrototypeUltimateTool/src/MainForm.cs:542-546
            // (two buttons only: "2D Текстура" / "3D Модель"); existing IDs/localized labels preserved.
            viewModeToolStrip.Items.AddRange(new ToolStripItem[]
            {
                mode2DButton, mode3DButton
            });

            // 2D viewer — ported from PrototypeUltimateTool MainForm.cs:548-577.
            // Prototype item order/IDs/appearance preserved; Bundle extras (V-Flip, Fit/100%) appended.
            channelToolStrip = new ToolStrip
            {
                Name = "toolbar.textureChannels", Dock = DockStyle.Top,
                GripStyle = ToolStripGripStyle.Hidden, Renderer = renderer, Visible = false
            };
            textureModeRgbaButton = new ToolStripButton(UiLocalization.TranslateId("channel.preset.rgba", "RGBA", _uiLanguage), null, (s, e) => SetChannels(true, true, true, true))
            {
                Name = "channel.preset.rgba", CheckOnClick = false, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold)
            };
            textureModeRgbButton = new ToolStripButton(UiLocalization.TranslateId("channel.preset.rgb", "RGB", _uiLanguage), null, (s, e) => SetChannels(true, true, true, false))
            {
                Name = "channel.preset.rgb", CheckOnClick = false
            };

            channelRButton = new ToolStripButton("R", null, (s, e) => ApplyChannels()) { Name = "channel.red", CheckOnClick = true, Checked = true, ForeColor = Color.IndianRed, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) };
            channelGButton = new ToolStripButton("G", null, (s, e) => ApplyChannels()) { Name = "channel.green", CheckOnClick = true, Checked = true, ForeColor = Color.LightGreen, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) };
            channelBButton = new ToolStripButton("B", null, (s, e) => ApplyChannels()) { Name = "channel.blue", CheckOnClick = true, Checked = true, ForeColor = Color.DodgerBlue, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) };
            channelAButton = new ToolStripButton("A", null, (s, e) => ApplyChannels()) { Name = "channel.alpha", CheckOnClick = true, Checked = true, ForeColor = Color.LightGray, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) };
            resolutionLabel = new ToolStripLabel { Name = "label.textureResolution", Alignment = ToolStripItemAlignment.Right };

            channelToolStrip.Items.AddRange(new ToolStripItem[]
            {
                new ToolStripLabel(UiLocalization.TranslateId("label.preset", "Preset:", _uiLanguage)) { Name = "label.preset" }, textureModeRgbaButton, textureModeRgbButton,
                new ToolStripSeparator(),
                new ToolStripLabel(UiLocalization.TranslateId("label.channels", "Channels:", _uiLanguage)) { Name = "label.channels" }, channelRButton, channelGButton, channelBButton, channelAButton,
                new ToolStripSeparator(), resolutionLabel
            });

            filePreviewTextBox = new TextBox
            {
                Name = "preview.fileText", Dock = DockStyle.Fill, Multiline = true,
                ReadOnly = true, WordWrap = false, ScrollBars = ScrollBars.Both,
                BorderStyle = BorderStyle.Fixed3D,
                Font = new Font("Consolas", 9F), Visible = false
            };
            model3DView = new Model3DView();
            var topViewPanel = new Panel { Dock = DockStyle.Fill };
            var previewToolbars = new Panel { Dock = DockStyle.Top, AutoSize = true };
            previewToolbars.Controls.Add(channelToolStrip);
            previewToolbars.Controls.Add(viewModeToolStrip);
            // Dock.Top order: lower index = front = docked last. We want viewModeToolStrip on top.
            previewToolbars.Controls.SetChildIndex(viewModeToolStrip, 1);
            previewToolbars.Controls.SetChildIndex(channelToolStrip, 0);
            topViewPanel.Controls.Add(previewScrollPanel);
            topViewPanel.Controls.Add(filePreviewTextBox);
            topViewPanel.Controls.Add(model3DView);
            topViewPanel.Controls.Add(previewToolbars);
            previewMessageLabel = new Label
            {
                Name = "preview.message", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(8), Visible = false
            };
            topViewPanel.Controls.Add(previewMessageLabel);
            // In WinForms index 0 is frontmost; the hint must sit above the PictureBox.
            previewMessageLabel.BringToFront();
            filePreviewTextBox.BringToFront();
            previewMessageLabel.BringToFront();

            propGrid = new ThemedPropertyGrid
            {
                Name = "properties.resource", Dock = DockStyle.Fill
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
                SplitterDistance = 480, SplitterWidth = 5
            };
            rightSplit.Panel1.Controls.Add(topViewPanel);
            rightSplit.Panel2.Controls.Add(propertyPanel);

            // Min sizes and SplitterDistance are applied in OnLoad, when the real client
            // width exists. Setting them here throws because the control is still tiny.
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
            statusProgress = new ToolStripProgressBar
            {
                Name = "status.progress", Style = ProgressBarStyle.Marquee,
                Width = 90, Visible = false
            };
            statusStrip.Items.AddRange(new ToolStripItem[]
            {
                statusStateLabel, statusProgress, statusPathLabel
            });

            Controls.Add(mainSplit);
            Controls.Add(statusStrip);
            Controls.Add(mainToolStrip);
            Controls.Add(menuStrip);

            // Explicit reading order: menu, toolbars, filter, tree, preview, properties, status.
            menuStrip.TabIndex = 0;
            mainToolStrip.TabIndex = 1;
            filterToolStrip.TabIndex = 2;
            nodeTreeView.TabIndex = 3;
            gameFileTreeView.TabIndex = 4;
            noResultsPanel.TabIndex = 5;
            viewModeToolStrip.TabIndex = 6;
            previewBox.TabIndex = 7;
            filePreviewTextBox.TabIndex = 8;
            propGrid.TabIndex = 9;
            statusStrip.TabIndex = 10;

            InitializeWorkspaceStates(topViewPanel, propertyBodyPanel);
            WireNativeTheme(this);

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
            EditorCommandChrome.Apply(channelToolStrip, false);
            EditorCommandChrome.Apply(treeContextMenu, false);
            DpiChanged += (s, e) =>
            {
                EditorCommandChrome.Apply(menuStrip, false);
                EditorCommandChrome.Apply(channelToolStrip, false);
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
