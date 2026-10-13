using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace GlitchForge
{
    public interface IMemoryTarget
    {
        string Name { get; }
        long Size { get; }
        bool Writable { get; }
        byte Read(long address);
        void Write(long address, byte value);
    }

    public enum Engine { RandomByte, BitFlip, Add, Subtract, SetByte }

    public sealed class BlastSettings
    {
        public long Start, End;
        public int Intensity = 8;
        public uint Seed = 1;
        public byte Value = 1;
        public Engine Engine;
    }

    [DataContract]
    public sealed class Mutation
    {
        [DataMember] public long Address;
        [DataMember] public byte Before;
        [DataMember] public byte After;
    }

    [DataContract]
    public sealed class BlastLayer
    {
        [DataMember] public int Version = 1;
        [DataMember] public string Name;
        [DataMember] public string GameHash;
        [DataMember] public string Domain;
        [DataMember] public long DomainSize;
        [DataMember] public uint Seed;
        [DataMember] public Engine Engine;
        [DataMember] public List<Mutation> Writes = new List<Mutation>();
        public override string ToString() { return Name + " · " + Domain + " · " + Writes.Count + " bytes"; }
    }

    // Specified PRNG rather than System.Random: the same seed works across runtimes.
    internal sealed class Rng
    {
        private uint state;
        public Rng(uint seed) { state = seed == 0 ? 0x9E3779B9u : seed; }
        public uint Next() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state; }
        public ulong Next64() { return ((ulong)Next() << 32) | Next(); }
        public long Below(long bound)
        {
            ulong n = (ulong)bound, threshold = unchecked(0UL - n) % n, value;
            do { value = Next64(); } while (value < threshold);
            return (long)(value % n);
        }
    }

    public static class Corruptor
    {
        public const int MaxWrites = 4096;

        public static BlastLayer Generate(IMemoryTarget target, BlastSettings settings, string gameHash)
        {
            if (!target.Writable) throw new InvalidOperationException("Memory domain is read-only.");
            if (settings.Start < 0 || settings.End < settings.Start || settings.End >= target.Size)
                throw new ArgumentException("Address range must fit the selected memory domain.");
            if (settings.Intensity < 1 || settings.Intensity > MaxWrites)
                throw new ArgumentException("Intensity must be between 1 and 4096.");
            if (!Enum.IsDefined(typeof(Engine), settings.Engine)) throw new ArgumentException("Unknown engine.");
            long length = settings.End - settings.Start + 1;
            int count = (int)Math.Min(length, settings.Intensity);
            var rng = new Rng(settings.Seed);
            // Partial Fisher-Yates through a sparse map avoids both large allocation and retry loops.
            var map = new Dictionary<long, long>();
            var layer = new BlastLayer { Name = DateTime.Now.ToString("HH:mm:ss") + " " + settings.Engine,
                GameHash = gameHash, Domain = target.Name, DomainSize = target.Size,
                Engine = settings.Engine, Seed = settings.Seed };
            for (int i = 0; i < count; i++)
            {
                long remaining = length - i, pick = rng.Below(remaining);
                long offset = map.TryGetValue(pick, out long found) ? found : pick;
                long tail = remaining - 1;
                map[pick] = map.TryGetValue(tail, out found) ? found : tail;
                long address = settings.Start + offset;
                byte before = target.Read(address), after;
                switch (settings.Engine)
                {
                    case Engine.BitFlip: after = (byte)(before ^ (1 << (int)(rng.Next() % 8))); break;
                    case Engine.Add: after = unchecked((byte)(before + settings.Value)); break;
                    case Engine.Subtract: after = unchecked((byte)(before - settings.Value)); break;
                    case Engine.SetByte: after = settings.Value; break;
                    default: after = (byte)rng.Next(); if (after == before) after ^= 1; break;
                }
                layer.Writes.Add(new Mutation { Address = address, Before = before, After = after });
            }
            return layer;
        }

        public static void Validate(BlastLayer layer, IMemoryTarget target, string hash)
        {
            if (layer == null || layer.Version != 1 || layer.Writes == null || layer.Writes.Count < 1 || layer.Writes.Count > MaxWrites)
                throw new ArgumentException("Invalid or unsupported blast layer.");
            if (string.IsNullOrEmpty(hash) || layer.GameHash != hash) throw new InvalidOperationException("Layer belongs to a different ROM.");
            if (!target.Writable || layer.Domain != target.Name || layer.DomainSize != target.Size)
                throw new InvalidOperationException("Layer memory domain does not match this core.");
            var addresses = new HashSet<long>();
            foreach (var write in layer.Writes)
                if (write == null || write.Address < 0 || write.Address >= target.Size || !addresses.Add(write.Address))
                    throw new ArgumentException("Layer contains duplicate or out-of-range addresses.");
        }

        // Replay captures fresh before-values so its undo never restores another session's memory.
        public static BlastLayer Apply(BlastLayer layer, IMemoryTarget target, string hash)
        {
            Validate(layer, target, hash);
            var applied = new BlastLayer { Name = layer.Name, GameHash = hash, Domain = target.Name,
                DomainSize = target.Size, Seed = layer.Seed, Engine = layer.Engine };
            // Read all original bytes before any writes. A failed read leaves memory untouched.
            foreach (var write in layer.Writes)
                applied.Writes.Add(new Mutation { Address = write.Address, Before = target.Read(write.Address), After = write.After });
            int index = 0;
            try
            {
                for (; index < applied.Writes.Count; index++)
                {
                    var write = applied.Writes[index];
                    target.Write(write.Address, write.After);
                }
            }
            catch (Exception original)
            {
                var errors = new List<Exception> { original };
                // Include the attempted write: an adapter can throw after changing its byte.
                for (int j = Math.Min(index, applied.Writes.Count - 1); j >= 0; j--)
                    try { target.Write(applied.Writes[j].Address, applied.Writes[j].Before); }
                    catch (Exception rollback) { errors.Add(rollback); }
                if (errors.Count > 1) throw new AggregateException("Blast failed; rollback was incomplete. Restore a savestate.", errors);
                throw new InvalidOperationException("Blast failed; original bytes were restored.", original);
            }
            return applied;
        }

        public static int Undo(BlastLayer layer, IMemoryTarget target, string hash)
        {
            Validate(layer, target, hash);
            int skipped = 0;
            foreach (var write in layer.Writes.AsEnumerable().Reverse())
            {
                // Game code may have changed this byte since the blast. Preserve that change.
                if (target.Read(write.Address) == write.After) target.Write(write.Address, write.Before);
                else skipped++;
            }
            return skipped;
        }

        public static void Save(string path, BlastLayer layer)
        {
            using (var stream = File.Create(path)) new DataContractJsonSerializer(typeof(BlastLayer)).WriteObject(stream, layer);
        }
        public static BlastLayer Load(string path)
        {
            if (new FileInfo(path).Length > 1024 * 1024) throw new ArgumentException("Layer file exceeds 1 MB.");
            using (var stream = File.OpenRead(path))
                return (BlastLayer)new DataContractJsonSerializer(typeof(BlastLayer)).ReadObject(stream);
        }
    }
}
