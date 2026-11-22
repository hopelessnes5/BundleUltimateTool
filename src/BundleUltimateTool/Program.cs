using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BundleUltimateTool
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            ApplicationLog.BeginSession();
            ApplicationLog.Write("STARTUP", "Process started; exe=" + Application.ExecutablePath +
                "; cwd=" + Environment.CurrentDirectory + "; commandLine=" + Environment.CommandLine);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += (s, e) =>
            {
                ApplicationLog.Write("UNHANDLED_UI_EXCEPTION", "Unhandled WinForms UI exception.", e.Exception);
                MessageBox.Show(e.Exception.ToString(), "BundleUltimateTool - UI error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Exception exception = e.ExceptionObject as Exception;
                ApplicationLog.Write("UNHANDLED_DOMAIN_EXCEPTION",
                    "Unhandled AppDomain exception; terminating=" + e.IsTerminating,
                    exception ?? new Exception(Convert.ToString(e.ExceptionObject)));
                MessageBox.Show(Convert.ToString(e.ExceptionObject), "BundleUltimateTool - fatal error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            try
            {
                string[] commandLineArgs = Environment.GetCommandLineArgs() ?? new string[0];
                string[] effectiveArgs = commandLineArgs.Length > 1
                    ? commandLineArgs.Skip(1).ToArray()
                    : (args ?? Array.Empty<string>());

                string gameFolder = null;
                int selectedEntry = -1;
                var fileArgs = new System.Collections.Generic.List<string>();
                for (int i = 0; i < effectiveArgs.Length; i++)
                {
                    if (string.Equals(effectiveArgs[i], "--game-folder", StringComparison.OrdinalIgnoreCase) &&
                        i + 1 < effectiveArgs.Length)
                    {
                        gameFolder = effectiveArgs[i + 1];
                        i++;
                    }
                    else if (string.Equals(effectiveArgs[i], "--select-entry", StringComparison.OrdinalIgnoreCase) &&
                             i + 1 < effectiveArgs.Length &&
                             int.TryParse(effectiveArgs[i + 1], out int entryIndex))
                    {
                        selectedEntry = entryIndex;
                        i++;
                    }
                    else fileArgs.Add(effectiveArgs[i]);
                }
                effectiveArgs = fileArgs.ToArray();

                string startupFile = ResolveStartupFile(effectiveArgs);
                ApplicationLog.Write("STARTUP_ARGUMENTS",
                    "Args=" + string.Join(" | ", effectiveArgs) +
                    "; ResolvedFile=" + (startupFile ?? "<none>") +
                    "; GameFolder=" + (gameFolder ?? "<none>"));

                if (effectiveArgs.Length > 0 && string.IsNullOrEmpty(startupFile))
                {
                    MessageBox.Show(
                        "BundleUltimateTool received startup arguments but could not find an existing file among them.\n\n" +
                        string.Join("\n", effectiveArgs) + "\n\nDetails were written to " + ApplicationLog.CurrentLogPath,
                        "BundleUltimateTool - cannot determine startup file",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                Application.Run(new MainForm(startupFile, gameFolder, selectedEntry));
                ApplicationLog.Write("SHUTDOWN", "Application main loop exited normally.");
            }
            catch (Exception ex)
            {
                ApplicationLog.Write("STARTUP_FAILED", "BundleUltimateTool failed to start.", ex);
                MessageBox.Show("BundleUltimateTool could not start.\n\n" + ex.Message + "\n\nDetails:\n" + ex,
                    "BundleUltimateTool - startup failure", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Windows Explorer passes the selected document path as the first argument.
        private static string ResolveStartupFile(string[] args)
        {
            if (args == null || args.Length == 0) return null;

            foreach (string argument in args)
            {
                if (string.IsNullOrWhiteSpace(argument)) continue;
                string candidate = Environment.ExpandEnvironmentVariables(argument.Trim().Trim('"'));
                try
                {
                    if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri fileUri) && fileUri.IsFile)
                        candidate = fileUri.LocalPath;
                    candidate = Path.GetFullPath(candidate);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
                catch (PathTooLongException) { }
                catch (UriFormatException) { }
            }
            return null;
        }
    }
}
