using System;
using System.IO;
using System.Linq;
using GlitchForge;

class Memory : IMemoryTarget
{
    public byte[] Bytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
    public string Name => "RAM";
    public long Size => Bytes.Length;
    public bool Writable { get; set; } = true;
    public long FailAt = -1;
    public byte Read(long a) => Bytes[a];
    public void Write(long a, byte b) { Bytes[a] = b; if (a == FailAt) { FailAt = -1; throw new IOException("Write failed after mutation"); } }
}
static class Program
{
    static int count;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
    static void Reject(Action action, string name) { try { action(); } catch { Check(true, name); return; } throw new Exception("Expected rejection: " + name); }
    static void Main()
    {
        var m = new Memory(); var s = new BlastSettings { Start = 32, End = 47, Intensity = 64, Seed = 778, Engine = Engine.BitFlip };
        var layer = Corruptor.Generate(m, s, "rom");
        Check(layer.Writes.Count == 16 && layer.Writes.Select(w => w.Address).Distinct().Count() == 16, "range saturation produces unique addresses");
        Check(layer.Writes.All(w => w.Address >= 32 && w.Address <= 47 && System.Numerics.BitOperations.PopCount((uint)(w.Before ^ w.After)) == 1), "bitflip changes exactly one bit inside bounds");
        var again = Corruptor.Generate(m, s, "rom");
        Check(layer.Writes.Zip(again.Writes).All(w => w.First.Address == w.Second.Address && w.First.After == w.Second.After), "seed is reproducible");
        var original = (byte[])m.Bytes.Clone(); var applied = Corruptor.Apply(layer, m, "rom");
        Check(applied.Writes.All(w => m.Read(w.Address) == w.After), "apply writes actual bytes");
        Check(Corruptor.Undo(applied, m, "rom") == 0 && original.SequenceEqual(m.Bytes), "undo restores exact original memory");
        applied = Corruptor.Apply(layer, m, "rom"); var first = applied.Writes[0]; m.Bytes[first.Address] = (byte)(first.After ^ 0x80);
        Check(Corruptor.Undo(applied, m, "rom") == 1 && m.Bytes[first.Address] == (byte)(first.After ^ 0x80), "undo preserves later game writes");
        m.Bytes = (byte[])original.Clone(); m.Bytes[first.Address] = 222;
        applied = Corruptor.Apply(layer, m, "rom"); Corruptor.Undo(applied, m, "rom");
        Check(m.Bytes[first.Address] == 222, "replay undo uses fresh before-values");
        m.Bytes = (byte[])original.Clone(); m.FailAt = layer.Writes[3].Address;
        Reject(() => Corruptor.Apply(layer, m, "rom"), "failed write is reported");
        Check(original.SequenceEqual(m.Bytes), "failed write rolls back including partially changed byte");
        Reject(() => Corruptor.Apply(layer, m, "other-rom"), "ROM mismatch rejected");
        layer.Writes[1].Address = layer.Writes[0].Address; Reject(() => Corruptor.Apply(layer, m, "rom"), "duplicate address rejected before writes");
        layer = again; layer.Writes[0].Address = 256; Reject(() => Corruptor.Apply(layer, m, "rom"), "out of bounds layer rejected");
        s.Start = -1; Reject(() => Corruptor.Generate(m, s, "rom"), "negative range rejected"); s.Start = 0; s.End = 256;
        Reject(() => Corruptor.Generate(m, s, "rom"), "range end rejected"); s.End = 255; s.Intensity = 0;
        Reject(() => Corruptor.Generate(m, s, "rom"), "zero intensity rejected"); s.Intensity = 4097;
        Reject(() => Corruptor.Generate(m, s, "rom"), "excess intensity rejected"); s.Intensity = 8; m.Writable = false;
        Reject(() => Corruptor.Generate(m, s, "rom"), "read-only domain rejected"); m.Writable = true;
        s.Start = s.End = 255; s.Intensity = 1; s.Engine = Engine.Add; s.Value = 1;
        Check(Corruptor.Generate(m, s, "rom").Writes[0].After == 0, "add wraps at 255");
        s.Start = s.End = 0; s.Engine = Engine.Subtract;
        Check(Corruptor.Generate(m, s, "rom").Writes[0].After == 255, "subtract wraps at zero");
        s.Engine = Engine.RandomByte; s.Start = 0; s.End = 255; s.Intensity = 256;
        Check(Corruptor.Generate(m, s, "rom").Writes.All(w => w.After != w.Before), "random engine always changes selected bytes");
        var path = Path.Combine(Path.GetTempPath(), "glitchforge-" + Guid.NewGuid() + ".json");
        try { Corruptor.Save(path, Corruptor.Generate(m, s, "rom")); var imported = Corruptor.Load(path); Corruptor.Validate(imported, m, "rom"); Check(imported.Writes.Count == 256 && imported.Seed == s.Seed, "JSON layer round-trip"); }
        finally { File.Delete(path); }
        Console.WriteLine(count + " checks passed.");
    }
}
