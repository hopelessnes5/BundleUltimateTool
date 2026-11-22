using System.Drawing;
using System.Windows.Forms;

namespace BundleUltimateTool.UI
{
    // Semantic roles shared by the existing shell. Keep concrete colors here;
    // a future WinUI shell maps these roles to ThemeResource keys (see DESIGN.md).
    internal sealed class EditorPalette
    {
        internal Color Canvas { get; private init; }
        internal Color Surface { get; private init; }
        internal Color Toolbar { get; private init; }
        internal Color Input { get; private init; }
        internal Color Text { get; private init; }
        internal Color SecondaryText { get; private init; }
        internal Color DisabledText { get; private init; }
        internal Color Border { get; private init; }
        internal Color Hover { get; private init; }
        internal Color Pressed { get; private init; }
        internal Color Selection { get; private init; }
        internal Color SelectionText { get; private init; }
        internal Color Accent { get; private init; }
        internal Color Preview { get; private init; }
        internal Color RedChannel { get; private init; }
        internal Color GreenChannel { get; private init; }
        internal Color BlueChannel { get; private init; }

        private static Color Rgb(int r, int g, int b) => Color.FromArgb(r, g, b);

        private static readonly EditorPalette Dark = new EditorPalette
        {
            Canvas = Rgb(28, 28, 28), Surface = Rgb(37, 37, 38),
            Toolbar = Rgb(40, 40, 42), Input = Rgb(50, 50, 50),
            Text = Rgb(242, 242, 242), SecondaryText = Rgb(184, 184, 184),
            DisabledText = Rgb(145, 145, 145), Border = Rgb(70, 70, 74),
            Hover = Rgb(62, 62, 64), Pressed = Rgb(48, 48, 50),
            Selection = Rgb(56, 78, 104), SelectionText = Color.White,
            Accent = Rgb(129, 195, 255), Preview = Rgb(16, 16, 16),
            RedChannel = Rgb(255, 176, 176), GreenChannel = Rgb(144, 238, 144),
            BlueChannel = Rgb(129, 195, 255)
        };

        private static readonly EditorPalette Light = new EditorPalette
        {
            Canvas = Rgb(243, 243, 243), Surface = Color.White,
            Toolbar = Rgb(249, 249, 249), Input = Color.White,
            Text = Rgb(26, 26, 26), SecondaryText = Rgb(89, 89, 89),
            DisabledText = Rgb(115, 115, 115), Border = Rgb(200, 200, 204),
            Hover = Rgb(229, 229, 232), Pressed = Rgb(216, 216, 220),
            Selection = Rgb(211, 228, 248), SelectionText = Rgb(26, 26, 26),
            Accent = Rgb(0, 84, 153), Preview = Rgb(235, 235, 235),
            RedChannel = Rgb(160, 32, 32), GreenChannel = Rgb(16, 104, 32),
            BlueChannel = Rgb(0, 84, 153)
        };

        internal static EditorPalette ForTheme(bool dark)
        {
            if (!SystemInformation.HighContrast) return dark ? Dark : Light;
            // Resolve current system colors each time; do not freeze a contrast theme.
            return new EditorPalette
            {
                Canvas = SystemColors.Control, Surface = SystemColors.Window,
                Toolbar = SystemColors.Control, Input = SystemColors.Window,
                Text = SystemColors.WindowText, SecondaryText = SystemColors.WindowText,
                DisabledText = SystemColors.GrayText, Border = SystemColors.WindowText,
                Hover = SystemColors.Highlight, Pressed = SystemColors.Highlight,
                Selection = SystemColors.Highlight, SelectionText = SystemColors.HighlightText,
                Accent = SystemColors.WindowText, Preview = SystemColors.Window,
                RedChannel = SystemColors.WindowText, GreenChannel = SystemColors.WindowText,
                BlueChannel = SystemColors.WindowText
            };
        }

        internal Color ItemText(string name, bool enabled, bool highlighted)
        {
            if (!enabled) return DisabledText;
            if (SystemInformation.HighContrast && highlighted) return SelectionText;
            return name switch
            {
                "channel.red" => RedChannel,
                "channel.green" => GreenChannel,
                "channel.blue" => BlueChannel,
                _ => highlighted ? SelectionText : Text
            };
        }
    }
}
