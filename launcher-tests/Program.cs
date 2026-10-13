using System;
using System.Drawing;
using System.IO;
using System.Linq;
using GlitchForge.Launcher;

class LauncherTests
{
    static int count;
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
    static void Reject(Action action, string label) { try { action(); } catch { Check(true, label); return; } throw new Exception("Expected rejection: " + label); }
    [STAThread]
    static void Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(root);
        string fixture = Path.Combine(root, "BizHawk with spaces"); Directory.CreateDirectory(Path.Combine(fixture, "dll"));
        File.Copy(Path.GetFullPath(args[1]), Path.Combine(fixture, "EmuHawk.exe"), true);
        foreach (var name in new[] { "BizHawk.Client.Common.dll", "BizHawk.Emulation.Common.dll", "BizHawk.Common.dll" }) File.WriteAllText(Path.Combine(fixture, "dll", name), "fixture");
        Reject(() => LauncherOperations.ValidateFolder(""), "empty folder rejected");
        Reject(() => LauncherOperations.ValidateFolder(root), "folder without emulator rejected");
        Check(LauncherOperations.ValidateFolder(fixture) == fixture, "valid distribution recognized");
        Check(LauncherOperations.PluginBytes().SequenceEqual(File.ReadAllBytes(args[2])), "embedded plugin exactly matches tested DLL");
        string installed = LauncherOperations.Install(fixture);
        Check(File.ReadAllBytes(installed).SequenceEqual(LauncherOperations.PluginBytes()), "plugin installed into ExternalTools");
        File.WriteAllText(installed, "old version"); LauncherOperations.Install(fixture);
        Check(File.ReadAllBytes(installed).SequenceEqual(LauncherOperations.PluginBytes()), "existing plugin replaced atomically");
        Check(Directory.GetFiles(Path.GetDirectoryName(installed), "*.tmp").Length == 0, "temporary installation files cleaned");
        Reject(() => LauncherOperations.Launch(fixture, Path.Combine(root, "missing.rom")), "missing ROM rejected");
        string game = Path.Combine(root, "game with spaces.rom"); File.WriteAllBytes(game, new byte[1]);
        using (var process = LauncherOperations.Launch(fixture, game)) { if (!process.WaitForExit(10000)) throw new Exception("Probe timed out"); }
        var probe = File.ReadAllLines(Path.Combine(fixture, "launch-probe.txt"));
        Check(probe.SequenceEqual(new[] { fixture, "--open-ext-tool-dll=GlitchForge", game }), "launch uses correct working directory and quotes ROM path");
        using (var process = LauncherOperations.Launch(fixture, "")) { if (!process.WaitForExit(10000)) throw new Exception("Probe timed out"); }
        Check(File.ReadAllLines(Path.Combine(fixture, "launch-probe.txt")).SequenceEqual(new[] { fixture }), "launch without ROM opens emulator without premature tool load");
        using (var form = new LauncherForm())
        {
            form.Opacity = 0; form.ShowInTaskbar = false; form.Show(); System.Windows.Forms.Application.DoEvents();
            using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(root, "launcher-preview.png")); }
            Check(form.Text.Contains("GlitchForge"), "launcher form renders");
        }
        Console.WriteLine(count + " launcher checks passed.");
    }
}
