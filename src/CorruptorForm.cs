using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using BizHawk.Client.Common;
using BizHawk.Client.EmuHawk;
using BizHawk.Emulation.Common;

namespace GlitchForge
{
    public sealed class BizHawkTarget : IMemoryTarget
    {
        private readonly MemoryDomain domain;
        public BizHawkTarget(MemoryDomain domain) { this.domain = domain; }
        public string Name => domain.Name;
        public long Size => domain.Size;
        public bool Writable => domain.Writable;
        public byte Read(long address) { domain.Enter(); try { return domain.PeekByte(address); } finally { domain.Exit(); } }
        public void Write(long address, byte value) { domain.Enter(); try { domain.PokeByte(address, value); } finally { domain.Exit(); } }
    }

    [ExternalTool("GlitchForge — Real-Time Corruptor")]
    public sealed class CorruptorForm : ToolFormBase, IExternalToolForm
    {
        [RequiredService] public IMemoryDomains MemoryDomains { get; set; }
        public ApiContainer APIs { get; set; }
        protected override string WindowTitleStatic => "GlitchForge | BizHawk Corruptor";
        private readonly ComboBox domains = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 310 };
        private readonly ComboBox engines = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        private readonly TextBox start = new TextBox { Text = "0", Width = 130 }, end = new TextBox { Width = 130 };
        private readonly NumericUpDown intensity = Number(1, 4096, 8), interval = Number(1, 3600, 30), value = Number(0, 255, 1), seed = Number(0, uint.MaxValue, 1);
        private readonly CheckBox advanceSeed = new CheckBox { Text = "Advance seed after each blast", AutoSize = true, Checked = true };
        private readonly Label status = new Label { AutoSize = true, MaximumSize = new Size(840, 0) };
        private readonly Label gameLabel = new Label { AutoSize = true }, domainLabel = new Label { AutoSize = true };
        private readonly Button autoButton = new Button { Text = "Start auto", Width = 125, Height = 35 };
        private readonly ListBox stockpile = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly ListView writes = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false };
        private readonly Stack<BlastLayer> history = new Stack<BlastLayer>();
        private BlastLayer last;
        private bool running;
        private int frames;
        private string baseline, baselineHash;
        private string Hash => Game?.Hash;
        private static readonly Color Ink = Color.FromArgb(224, 232, 243), PanelColor = Color.FromArgb(28, 34, 46);

        public CorruptorForm()
        {
            ClientSize = new Size(940, 760); MinimumSize = new Size(850, 700);
            Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(17, 22, 31); ForeColor = Ink;
            AutoScaleMode = AutoScaleMode.Dpi;
            engines.Items.AddRange(Enum.GetNames(typeof(Engine))); engines.SelectedIndex = 1;
            writes.Columns.Add("Address", 135); writes.Columns.Add("Before", 80); writes.Columns.Add("After", 80);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 7 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            var heading = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
            heading.Controls.Add(new Label { Text = "GLITCHFORGE", Font = new Font("Segoe UI", 23, FontStyle.Bold), AutoSize = true, ForeColor = Color.FromArgb(95, 233, 195) });
            heading.Controls.Add(gameLabel); root.Controls.Add(heading);
            var memory = Row(); memory.Controls.Add(Field("Writable memory domain", domains)); memory.Controls.Add(Field("Start address · hex", start)); memory.Controls.Add(Field("End address · hex, inclusive", end)); memory.Controls.Add(domainLabel); root.Controls.Add(memory);
            var settings = Row(); settings.Controls.Add(Field("Engine", engines)); settings.Controls.Add(Field("Bytes per blast", intensity)); settings.Controls.Add(Field("Every N frames", interval)); settings.Controls.Add(Field("Operand · decimal", value)); settings.Controls.Add(Field("Seed", seed)); settings.Controls.Add(advanceSeed); root.Controls.Add(settings);
            var blastRow = Row();
            blastRow.Controls.Add(ActionButton("Blast once", () => Blast(), true));
            autoButton.Click += (s, e) => Guard(() => { if (!running) Settings(); SetRunning(!running); }); blastRow.Controls.Add(autoButton);
            blastRow.Controls.Add(ActionButton("Undo last", Undo));
            blastRow.Controls.Add(ActionButton("Stash last", Stash));
            blastRow.Controls.Add(ActionButton("Refresh domains", () => Restart())); root.Controls.Add(blastRow);
            var stateRow = Row(); stateRow.Controls.Add(ActionButton("Save baseline…", SaveBaseline)); stateRow.Controls.Add(ActionButton("Restore baseline", RestoreBaseline));
            stateRow.Controls.Add(new Label { Text = "Save a baseline before experimenting. Undo only restores unchanged bytes.", AutoSize = true, Padding = new Padding(5, 10, 0, 0) }); root.Controls.Add(stateRow);
            var lower = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            lower.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); lower.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            lower.Controls.Add(new Label { Text = "LAST BLAST / BYTE CHANGES", AutoSize = true }, 0, 0);
            lower.Controls.Add(new Label { Text = "STOCKPILE / REPLAYABLE LAYERS", AutoSize = true }, 1, 0); lower.Controls.Add(writes, 0, 1);
            var stashPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            stashPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); stashPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 88)); stashPanel.Controls.Add(stockpile);
            var stashActions = Row(); stashActions.Controls.Add(ActionButton("Replay", Replay)); stashActions.Controls.Add(ActionButton("Export…", Export)); stashActions.Controls.Add(ActionButton("Import…", Import)); stashActions.Controls.Add(ActionButton("Remove", () => { if (stockpile.SelectedIndex >= 0) stockpile.Items.RemoveAt(stockpile.SelectedIndex); }));
            stashPanel.Controls.Add(stashActions); lower.Controls.Add(stashPanel, 1, 1); root.Controls.Add(lower); root.Controls.Add(status); Controls.Add(root);
            foreach (var control in new Control[] { domains, engines, start, end, stockpile, writes, intensity, interval, value, seed }) { control.BackColor = PanelColor; control.ForeColor = Ink; }
            domains.SelectedIndexChanged += (s, e) => { SetRunning(false); if (SelectedDomain != null) { start.Text = "0"; end.Text = (SelectedDomain.Size - 1).ToString("X"); domainLabel.Text = SelectedDomain.Size.ToString("N0") + " bytes"; } };
            FormClosing += (s, e) => SetRunning(false);
            Say("Load a game, select RAM, save a baseline, then blast.");
        }

        private static NumericUpDown Number(decimal min, decimal max, decimal initial) => new NumericUpDown { Minimum = min, Maximum = max, Value = initial, Width = max > 4096 ? 150 : 105 };
        private static FlowLayoutPanel Row() => new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 3, 0, 0) };
        private static Control Field(string label, Control input)
        {
            var field = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 14, 7) };
            field.Controls.Add(new Label { Text = label, AutoSize = true }); field.Controls.Add(input); return field;
        }
        private Button ActionButton(string text, Action action, bool primary = false)
        {
            var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(95, 35), FlatStyle = FlatStyle.Flat, BackColor = primary ? Color.FromArgb(43, 144, 119) : PanelColor, ForeColor = Ink };
            b.Click += (s, e) => Guard(action); return b;
        }
        private void Say(string message) { status.Text = (running ? "● AUTO RUNNING  |  " : "● IDLE  |  ") + message; }
        private void Guard(Action action)
        {
            try { action(); }
            catch (Exception e) { SetRunning(false); Say(e.Message); }
        }
        private MemoryDomain SelectedDomain => domains.SelectedItem as MemoryDomain;
        private BizHawkTarget Target()
        {
            if (SelectedDomain == null || string.IsNullOrEmpty(Hash) || Game.System == "NULL") throw new InvalidOperationException("Load a game with writable memory first.");
            return new BizHawkTarget(SelectedDomain);
        }
        private static long Hex(string text)
        {
            text = text.Trim(); if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text.Substring(2);
            if (!long.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long address) || address < 0) throw new ArgumentException("Enter a valid hexadecimal address.");
            return address;
        }
        private BlastSettings Settings()
        {
            var target = Target();
            var s = new BlastSettings { Start = Hex(start.Text), End = Hex(end.Text), Intensity = (int)intensity.Value, Seed = (uint)seed.Value, Value = (byte)value.Value, Engine = (Engine)engines.SelectedIndex };
            if (s.End < s.Start || s.End >= target.Size) throw new ArgumentException("Range must fit the selected domain.");
            return s;
        }
        private void SetRunning(bool enabled) { running = enabled; frames = 0; autoButton.Text = enabled ? "Stop auto" : "Start auto"; Say(enabled ? "Waiting for emulated frames…" : "Auto stopped."); }
        public override void Restart()
        {
            SetRunning(false); history.Clear(); last = null; writes.Items.Clear(); baseline = baselineHash = null;
            domains.Items.Clear();
            if (MemoryDomains != null) foreach (var d in MemoryDomains.Where(d => d.Writable && d.Size > 0)) domains.Items.Add(d);
            if (MemoryDomains?.MainMemory != null && domains.Items.Contains(MemoryDomains.MainMemory)) domains.SelectedItem = MemoryDomains.MainMemory;
            else if (domains.Items.Count > 0) domains.SelectedIndex = 0;
            gameLabel.Text = Game == null || Game.System == "NULL" ? "No game loaded" : Game.Name + "  /  " + Game.System;
            Say("Ready. Intensity is unique bytes; addresses are hexadecimal.");
        }
        protected override void UpdateBefore() { Tick(); }
        protected override void FastUpdateBefore() { Tick(); }
        private void Tick()
        {
            if (running && ++frames >= interval.Value) { frames = 0; Guard(() => Blast()); }
        }
        private void Remember(BlastLayer layer)
        {
            if (history.Count >= 64) { var keep = history.Take(63).Reverse().ToArray(); history.Clear(); foreach (var entry in keep) history.Push(entry); }
            history.Push(layer); last = layer; ShowLayer(layer);
        }
        private void ShowLayer(BlastLayer layer)
        {
            writes.BeginUpdate(); writes.Items.Clear();
            foreach (var w in layer.Writes.Take(256)) writes.Items.Add(new ListViewItem(new[] { "0x" + w.Address.ToString("X"), w.Before.ToString("X2"), w.After.ToString("X2") }));
            writes.EndUpdate();
        }
        private void Blast()
        {
            var settings = Settings(); var target = Target();
            Remember(Corruptor.Apply(Corruptor.Generate(target, settings, Hash), target, Hash));
            if (advanceSeed.Checked) seed.Value = unchecked(settings.Seed + 1);
            Say(last.Writes.Count + " bytes applied · seed " + last.Seed + " · " + last.Engine + (last.Writes.Count > 256 ? " · showing first 256" : ""));
        }
        private void Undo()
        {
            SetRunning(false); if (history.Count == 0) throw new InvalidOperationException("No blast to undo.");
            var layer = history.Peek(); var domain = MemoryDomains[layer.Domain];
            if (domain == null) throw new InvalidOperationException("Domain no longer exists.");
            int skipped = Corruptor.Undo(layer, new BizHawkTarget(domain), Hash); history.Pop();
            last = history.Count > 0 ? history.Peek() : null; writes.Items.Clear(); if (last != null) ShowLayer(last);
            Say("Undo complete; " + skipped + " bytes changed by the game were preserved. Use baseline restore for full recovery.");
        }
        private void Stash()
        {
            if (last == null) throw new InvalidOperationException("Blast first, then stash the result.");
            AddStash(last); Say("Layer stashed. Export it to keep it after closing.");
        }
        private void AddStash(BlastLayer layer) { if (stockpile.Items.Count >= 64) throw new InvalidOperationException("Stockpile limit is 64 layers. Export or remove a layer first."); stockpile.SelectedIndex = stockpile.Items.Add(layer); }
        private BlastLayer SelectedLayer() => stockpile.SelectedItem as BlastLayer ?? throw new InvalidOperationException("Select a stockpile layer first.");
        private void Replay()
        {
            SetRunning(false); var layer = SelectedLayer(); var domain = MemoryDomains[layer.Domain];
            if (domain == null) throw new InvalidOperationException("Layer domain is unavailable in this core.");
            Remember(Corruptor.Apply(layer, new BizHawkTarget(domain), Hash)); Say("Replayed " + layer.Writes.Count + " bytes. Restore the same baseline before replay for comparable results.");
        }
        private void Export()
        {
            var layer = SelectedLayer(); using (var dialog = new SaveFileDialog { Filter = "Blast layer (*.json)|*.json", FileName = "blast-layer.json" })
                if (dialog.ShowDialog(this) == DialogResult.OK) { Corruptor.Save(dialog.FileName, layer); Say("Exported " + Path.GetFileName(dialog.FileName)); }
        }
        private void Import()
        {
            using (var dialog = new OpenFileDialog { Filter = "Blast layer (*.json)|*.json" })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    var layer = Corruptor.Load(dialog.FileName); var domain = MemoryDomains?[layer.Domain ?? ""];
                    if (domain == null) throw new InvalidOperationException("Load the matching ROM and core before importing.");
                    Corruptor.Validate(layer, new BizHawkTarget(domain), Hash); AddStash(layer); Say("Imported layer. Select Replay to apply it.");
                }
        }
        private void SaveBaseline()
        {
            SetRunning(false); Target();
            using (var dialog = new SaveFileDialog { Filter = "BizHawk savestate (*.State)|*.State", FileName = "GlitchForge-baseline.State" })
                if (dialog.ShowDialog(this) == DialogResult.OK) { APIs.SaveState.Save(dialog.FileName); baseline = dialog.FileName; baselineHash = Hash; Say("Baseline saved. Restore it to recover the complete emulator state."); }
        }
        private void RestoreBaseline()
        {
            SetRunning(false);
            if (baseline == null || baselineHash != Hash) throw new InvalidOperationException("Save a baseline for this ROM first.");
            if (!APIs.SaveState.Load(baseline)) throw new InvalidOperationException("BizHawk could not restore the baseline.");
            history.Clear(); last = null; writes.Items.Clear(); Say("Baseline restored. Stockpile layers are ready to replay.");
        }
    }
}
