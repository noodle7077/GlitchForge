using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace GlitchForge.Launcher
{
    public static class LauncherOperations
    {
        public static string ValidateFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Choose your extracted BizHawk 2.11.1 folder first.");
            string full = Path.GetFullPath(folder.Trim());
            if (!File.Exists(Path.Combine(full, "EmuHawk.exe"))) throw new ArgumentException("This folder does not contain EmuHawk.exe.");
            foreach (string name in new[] { "BizHawk.Client.Common.dll", "BizHawk.Emulation.Common.dll", "BizHawk.Common.dll" })
                if (!File.Exists(Path.Combine(full, "dll", name))) throw new ArgumentException("BizHawk is missing dll/" + name + ". Extract the complete distribution first.");
            return full;
        }

        public static byte[] PluginBytes()
        {
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("GlitchForge.Plugin.dll"))
            {
                if (resource == null) throw new InvalidOperationException("The embedded corruption tool is missing.");
                using (var output = new MemoryStream()) { resource.CopyTo(output); return output.ToArray(); }
            }
        }

        public static string Install(string folder)
        {
            string full = ValidateFolder(folder);
            string tools = Path.Combine(full, "ExternalTools");
            Directory.CreateDirectory(tools);
            string destination = Path.Combine(tools, "GlitchForge.dll");
            byte[] bytes = PluginBytes();
            // Writing through a sibling temp file avoids leaving a truncated plugin if copying fails.
            string temporary = Path.Combine(tools, "GlitchForge-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return destination;
        }

        public static Process Launch(string folder, string rom)
        {
            string full = ValidateFolder(folder);
            string arguments = "";
            if (!string.IsNullOrWhiteSpace(rom))
            {
                string game = Path.GetFullPath(rom.Trim());
                if (!File.Exists(game)) throw new ArgumentException("The selected ROM file does not exist.");
                arguments = "--open-ext-tool-dll=GlitchForge \"" + game + "\"";
            }
            return Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(full, "EmuHawk.exe"), WorkingDirectory = full,
                Arguments = arguments, UseShellExecute = false
            });
        }
    }

    public sealed class LauncherForm : Form
    {
        private readonly TextBox folder = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox rom = new TextBox { Dock = DockStyle.Fill };
        private readonly Label status = new Label { AutoSize = true, MaximumSize = new Size(670, 0), Text = "Choose BizHawk, then install or launch." };
        private static readonly Color Background = Color.FromArgb(17, 22, 31), Panel = Color.FromArgb(28, 34, 46), TextColor = Color.FromArgb(224, 232, 243);

        public LauncherForm()
        {
            Text = "GlitchForge — Install & Launch"; ClientSize = new Size(740, 450);
            MinimumSize = new Size(700, 460); Font = new Font("Segoe UI", 10);
            StartPosition = FormStartPosition.CenterScreen; BackColor = Background; ForeColor = TextColor;
            AutoScaleMode = AutoScaleMode.Dpi;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 7 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            root.Controls.Add(new Label { Text = "GLITCHFORGE", Font = new Font("Segoe UI", 24, FontStyle.Bold), ForeColor = Color.FromArgb(95, 233, 195), AutoSize = true });
            root.Controls.Add(new Label { Text = "Real-time corruption for BizHawk 2.11.1\nThe tool runs inside the emulator. This EXE installs it and starts your game.", AutoSize = true });
            root.Controls.Add(PathRow("BizHawk folder · contains EmuHawk.exe", folder, ChooseFolder));
            root.Controls.Add(PathRow("Game / ROM · optional", rom, ChooseRom));
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
            buttons.Controls.Add(Button("Install & launch", () => Install(true), true));
            buttons.Controls.Add(Button("Install tool only", () => Install(false)));
            buttons.Controls.Add(Button("How to use", Help)); root.Controls.Add(buttons);
            root.Controls.Add(status);
            var link = new LinkLabel { Text = "Download BizHawk 2.11.1", AutoSize = true, LinkColor = Color.FromArgb(95, 233, 195) };
            link.LinkClicked += (s, e) => Guard(() => Process.Start(new ProcessStartInfo("https://github.com/TASEmulators/BizHawk/releases/tag/2.11.1") { UseShellExecute = true }));
            root.Controls.Add(link); Controls.Add(root);
            foreach (var input in new[] { folder, rom }) { input.BackColor = Panel; input.ForeColor = TextColor; }
            string nextToExe = AppDomain.CurrentDomain.BaseDirectory;
            if (File.Exists(Path.Combine(nextToExe, "EmuHawk.exe"))) folder.Text = nextToExe;
            else if (File.Exists(Path.Combine(nextToExe, "BizHawk", "EmuHawk.exe"))) folder.Text = Path.Combine(nextToExe, "BizHawk");
        }

        private Control PathRow(string label, TextBox input, Action browse)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 0, 0, 12) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); row.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            var title = new Label { Text = label, AutoSize = true }; row.Controls.Add(title, 0, 0); row.SetColumnSpan(title, 2);
            row.Controls.Add(input, 0, 1); row.Controls.Add(Button("Browse…", browse), 1, 1); return row;
        }
        private Button Button(string text, Action action, bool primary = false)
        {
            var result = new Button { Text = text, AutoSize = true, MinimumSize = new Size(96, 34), FlatStyle = FlatStyle.Flat, ForeColor = TextColor, BackColor = primary ? Color.FromArgb(43, 144, 119) : Panel };
            result.Click += (s, e) => Guard(action); return result;
        }
        private void Guard(Action action)
        {
            try { action(); }
            catch (IOException ex) { status.Text = "Could not install or launch. Close BizHawk before updating its tool. " + ex.Message; }
            catch (Exception ex) { status.Text = ex.Message; }
        }
        private void ChooseFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Choose the extracted BizHawk 2.11.1 folder", SelectedPath = Directory.Exists(folder.Text) ? folder.Text : "" })
                if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath;
        }
        private void ChooseRom()
        {
            using (var dialog = new OpenFileDialog { Title = "Select a game or ROM", Filter = "All files (*.*)|*.*", CheckFileExists = true })
                if (dialog.ShowDialog(this) == DialogResult.OK) rom.Text = dialog.FileName;
        }
        private void Install(bool launch)
        {
            if (launch && !string.IsNullOrWhiteSpace(rom.Text) && !File.Exists(rom.Text.Trim())) throw new ArgumentException("Choose an existing ROM file first.");
            string installed = LauncherOperations.Install(folder.Text);
            if (!launch) { status.Text = "Installed " + installed + ". Load your game, then open Tools → External Tools → GlitchForge."; return; }
            LauncherOperations.Launch(folder.Text, rom.Text);
            status.Text = string.IsNullOrWhiteSpace(rom.Text)
                ? "BizHawk launched. Load a game, then choose Tools → External Tools → GlitchForge."
                : "Game and corruptor launched. Accept BizHawk's external-tool prompt, then save a baseline before blasting.";
        }
        private void Help()
        {
            MessageBox.Show(this,
                "1. Download and extract stock BizHawk 2.11.1 for Windows.\n2. Choose its folder and optionally a ROM here.\n3. Select Install & launch. Accept BizHawk's standard tool prompt.\n4. In GlitchForge, save a baseline and start with 1–8 BitFlip bytes.\n5. Blast once or start auto; restore the baseline for full recovery.\n\nThe corruption controls run inside BizHawk. The EXE embeds the plugin and does not include an emulator or ROMs. Existing RTC-modified versions may require recompilation.",
                "Using GlitchForge", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LauncherForm());
        }
    }
}
