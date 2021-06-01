using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace leveledlistresolver
{
    public class LeveledItemGraph : MajorRecordGraphBase<ISkyrimMod, ISkyrimModGetter, ILeveledItem, ILeveledItemGetter>
    {
        public LeveledItemGraph(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, in FormKey formKey) : base(state, formKey) { }

        public IObjectBoundsGetter GetObjectBounds()
        {
            return ExtentRecords.LastOrDefault(record => record.ObjectBounds != Base.ObjectBounds)?.ObjectBounds ?? Base.ObjectBounds;
        }

        public byte GetChanceNone()
        {
            return ExtentRecords.LastOrDefault(record => record.ChanceNone != Base.ChanceNone)?.ChanceNone ?? Base.ChanceNone;
        }

        public LeveledItem.Flag GetFlags()
        {
            return ExtentRecords.LastOrDefault(record => record.Flags != Base.Flags)?.Flags ?? Base.Flags;
        }

        public IFormLinkGetter<IGlobalGetter> GetGlobal()
        {
            return ExtentRecords.LastOrDefault(record => record.Global != Base.Global)?.Global ?? Base.Global;
        }

        public IEnumerable<LeveledItemEntry> GetEntries()
        {
            //Todo: If ExtentRecords.Count is 1, find entries that were removed to get to it
            if (ExtentRecords.Count is 1)
            {
                if (ExtentRecords.Single().Entries is { } entries)
                {
                    foreach (var item in entries)
                    {
                        yield return item.DeepCopy();
                    }
                }
                yield break;
            }

            var baseEntries = ExtentBase.Entries ?? Array.Empty<ILeveledItemEntryGetter>();
            var entriesList = ExtentRecords.Select(list => list.Entries?.ToList() ?? new());

            List<LeveledItemEntry> yieldedItems = new();
            List<LeveledItemEntry> extraItems = new();

            foreach (var entries in entriesList)
            {
                foreach (var item in entries.Without(baseEntries).Without(yieldedItems))
                {
                    if (item.IsNullOrEmptySublist(linkCache) is false)
                    {
                        var _item = item.DeepCopy();
                        if (yieldedItems.Count < 254)
                        {
                            yieldedItems.Add(_item);
                            yield return _item;
                        }
                        else
                        {
                            extraItems.Add(_item);
                        }
                    }
                }
            }

            var commonItems = entriesList.Aggregate(ImmutableList.CreateRange(baseEntries), static (list, items) =>
            {
                var toRemove = items;
                return list.FindAll(toRemove.Remove);
            }).RemoveAll(entry =>  entry.IsNullOrEmptySublist(linkCache));

            foreach (var item in commonItems)
            {
                var _item = item.DeepCopy();
                if (yieldedItems.Count < 254)
                {
                    yieldedItems.Add(_item);
                    yield return _item;
                    continue;
                }
                extraItems.Add(_item);
            }

            if (extraItems.Any())
            {
                Console.WriteLine($"{GetEditorID()} had more than 255 entries.");
                yield return _(extraItems.ToArray());
            }

            LeveledItemEntry _(LeveledItemEntry[] items, uint depth = 1) 
            {
                var leveledItem = patchMod.LeveledItems.AddNew();
                leveledItem.EditorID = $"Mir_{GetEditorID()}_Sublist_{depth}";
                leveledItem.Entries = new(items.Length > 255 ? items[0..254].Append(_(items[255..], ++depth)) : items);
                leveledItem.Flags = GetFlags();
                leveledItem.Global.SetTo(GetGlobal());
                return new() { Data = new() { Reference = leveledItem.AsLink(), Level = 1, Count = 1 } };
            }
        }

        public override LeveledItem ToMajorRecord()
        {
            var record = Base.DeepCopy();
            record.FormVersion = GetFormVersion();
            record.EditorID = GetEditorID();
            record.ChanceNone = GetChanceNone();
            record.Flags = GetFlags();
            record.Global.SetTo(GetGlobal());
            record.Entries = new(GetEntries());
            return record;
        }
    }
}
