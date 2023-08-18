using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace leveledlistresolver
{
    sealed partial class RecordPatcher : IRecordPatcher<ILeveledNpcGetter, LeveledNpc>
    {
        static readonly LeveledNpc.TranslationMask LvlnMask = new(true)
        {
            FormVersion = false,
            VersionControl = false,
            Version2 = false,
            Entries = false
        };

        static readonly LeveledNpc.TranslationMask LvlnMask2 = new(false)
        {
            ChanceNone = true,
            Flags = true,
            Global = true
        };

        bool IRecordPatcher<ILeveledNpcGetter, LeveledNpc>.Try(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, FormKey formKey, out LeveledNpc? setter)
        {
            setter = default;

            var extentContexts = Program.LinkCache.GetExtentContexts<ILeveledNpcGetter>(formKey).ToArray();
            if (extentContexts.Length < 2)
            {
                var winning = extentContexts[0].Record;
                if (winning.Entries?.Any(static i => i.IsNullEntry()) ?? false)
                {
                    setter = winning.DeepCopy();
                    Console.WriteLine($"Removed {setter.Entries!.RemoveAll(Utility.IsNullEntry)} null entries from {setter.EditorID} [{formKey}]{Environment.NewLine}");
                    return true;
                }

                return false;
            }

            var highest = extentContexts[0].Record;
            var lowest = Program.LinkCache.GetLowestOverride<ILeveledNpcGetter>(formKey);

            var copy = highest.DeepCopy();
            copy.FormVersion = 44;
            copy.VersionControl = Utility.Timestamp;
            copy.Entries = new();

            bool a = !string.Equals(lowest.EditorID, copy.EditorID, StringComparison.InvariantCulture);
            bool b = !copy.Equals(lowest, LvlnMask2);

            if (string.IsNullOrWhiteSpace(copy.EditorID))
            {
                copy.EditorID = Guid.NewGuid().ToString("n");
                a = true;
            }

            foreach (var (_, record) in extentContexts[1..])
            {
                if (!a && !string.Equals(lowest.EditorID, record.EditorID, StringComparison.InvariantCulture))
                {
                    copy.EditorID = record.EditorID;
                    a = true;
                }

                if (!b && !lowest.Equals(record, LvlnMask2))
                {
                    copy.ChanceNone = record.ChanceNone;
                    copy.Flags = record.Flags;
                    copy.Global.SetTo(record.Global);
                    b = true;
                }
            }

            List<ILeveledNpcEntryGetter> entries = new();
            if (lowest.Entries is { Count: > 0 } && extentContexts.All(static i => i.Record.Entries is { Count: > 0 }))
            {
                var e = (IEnumerable<ILeveledNpcEntryGetter>)lowest.Entries!;
                var intersection = extentContexts.Aggregate(e, (i, k) => i.IntersectExt(k.Record.Entries));
                entries.AddRange(intersection);
            }

            var disjunction = extentContexts.Aggregate(Enumerable.Empty<ILeveledNpcEntryGetter>(), (i, k) => i.Concat(k.Record.Entries?.DisjunctLeft(lowest.Entries).DisjunctLeft(i) ?? Enumerable.Empty<ILeveledNpcEntryGetter>()));
            entries.AddRange(disjunction);
            if (Program.Settings.RemoveEmptySublists)

                entries.RemoveAll(i => i.IsNullOrEmptySublist(Program.LinkCache));
            else
                entries.RemoveAll(Utility.IsNullEntry);
            entries.Sort(static (i, k) => (i.Data?.Level ?? 0).CompareTo(k.Data?.Level));

            if (entries.Count > 255)
            {
                int i = 1;
                foreach (var chunk in entries.Chunk(255))
                {
                    LeveledNpc lvln = new(state.PatchMod, $"{copy.EditorID}Chunk_{i}")
                    {
                        FormVersion = 44,
                        VersionControl = Utility.Timestamp,
                        ChanceNone = copy.ChanceNone,
                        Flags = copy.Flags,
                        Global = copy.Global.AsNullable(),
                        Entries = chunk.Select(static i => i.DeepCopy()).ToExtendedList()
                    };

                    state.PatchMod.LeveledNpcs.Add(lvln);

                    LeveledNpcEntry entry = new()
                    {
                        Data = new() { Level = 1, Reference = lvln.ToLink(), Count = 1 }
                    };

                    copy.Entries.Add(entry);
                    i++;
                }
            }
            else
            {
                copy.Entries.AddRange(entries.ConvertAll(static i => i.DeepCopy()));
            }

            var modKeys = state.LoadOrder.ListedOrder
                .Where(i => i.Mod?.LeveledNpcs.ContainsKey(formKey) ?? false)
                .Select(static i => i.ModKey).ToHashSet();

            Console.WriteLine($"{copy.EditorID} [{formKey}]");
            foreach (var ctx in extentContexts.Reverse())
            {
                var masters = state.LoadOrder[ctx.ModKey].Mod?.MasterReferences.Select(static i => i.Master).Where(modKeys.Contains);
                if (masters != null)
                    Console.WriteLine($"{string.Join(" -> ", masters)} -> {ctx.ModKey}");
            }

            if (copy.Equals(highest, LvlnMask) && Utility.UnsortedEqual(copy.Entries, highest.Entries))
            {
                Console.WriteLine($"Skipped {copy.EditorID}\n");
                return false;
            }

            Console.WriteLine();
            setter = copy;
            return true;
        }
    }
}
