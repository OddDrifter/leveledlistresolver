using Mutagen.Bethesda;
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

            List<LeveledItemEntry> itemsYielded = new();
            List<LeveledItemEntry> extraItems = new();

            foreach (var entries in entriesList)
            {
                var entriesAdded = entries.Without(baseEntries).Without(itemsYielded).ToImmutableArray();
                foreach (var item in entriesAdded)
                {
                    var _item = item.DeepCopy();
                    if (itemsYielded.Count < 254)
                    {
                        itemsYielded.Add(_item);
                        yield return _item;
                    }
                    else
                    {
                        extraItems.Add(_item);
                    }
                }
            }

            var itemsIntersected = entriesList.Aggregate(ImmutableList.CreateRange(baseEntries), static (list, items) =>
            {
                var toRemove = items;
                return list.FindAll(toRemove.Remove);
            });

            foreach (var item in itemsIntersected)
            {
                var _item = item.DeepCopy();
                if (itemsYielded.Count < 254)
                {
                    itemsYielded.Add(_item);
                    yield return _item;
                    continue;
                }
                extraItems.Add(_item);
            }

            if (extraItems.Any())
            {
                Console.WriteLine($"{GetEditorID()} had more than 255 items.");
                yield return _(extraItems.ToArray());
            }

            LeveledItemEntry _(ILeveledItemEntryGetter[] items, uint depth = 1) 
            {
                var entries = items.Length switch
                {
                    > 255 => items[0..254].And(_(items[255..], depth + 1)),
                    _ => items
                };

                var leveledItem = patchMod.LeveledItems.AddNew();
                leveledItem.EditorID = $"Mir_{GetEditorID()}_Sublist_{depth}";
                leveledItem.Entries = new(entries.Select(r => r.DeepCopy()));
                leveledItem.Flags = GetFlags();
                leveledItem.Global.SetTo(GetGlobal());
                return new() { Data = new() { Reference = leveledItem.AsLink(), Level = 1, Count = 1 } };
            }
        }

        /*public ImmutableList<ILeveledItemEntryGetter> GetEntries()
        {
            if (ExtentRecords.Count is 1)
                return ExtentRecords.Single().Entries?.ToImmutableList() ?? ImmutableList<ILeveledItemEntryGetter>.Empty;

            var baseEntries = ExtentBase.Entries ?? Array.Empty<ILeveledItemEntryGetter>();
            var entriesList = ExtentRecords.Select(list => list.Entries?.ToList() ?? new());
           
            var added = entriesList.Aggregate(ImmutableList.CreateBuilder<ILeveledItemEntryGetter>(), (builder, items) =>
            {
                builder.AddRange(items.Without(baseEntries).Without(builder));
                return builder;
            }).ToImmutable();

            var intersection = entriesList.Aggregate(ImmutableList.CreateRange(baseEntries), static (list, items) =>
            {
                var toRemove = items;
                return list.FindAll(toRemove.Remove);
            });

            var items = added.AddRange(intersection).RemoveAll(entry => entry.IsNullOrEmptySublist(linkCache));

            if (items.Count > 255)
            {
                Console.WriteLine($"{GetEditorID()} had more than 255 items.");

                var segments = ((items.Count - 255) / 255) + 1;
                var extraItems = items.RemoveRange(0, 255 - segments);
                
                var entries = extraItems.Batch(255).Select((items, index) => 
                {
                    var leveledItem = patchMod.LeveledItems.AddNew();
                    leveledItem.EditorID = $"Mir_{GetEditorID()}_Sublist_{index + 1}";
                    leveledItem.Entries = items.Select(r => r.DeepCopy()).ToExtendedList();
                    leveledItem.Flags = GetFlags();
                    leveledItem.Global.SetTo(GetGlobal());

                    return new LeveledItemEntry()
                    {
                        Data = new() { Reference = leveledItem.AsLink(), Level = 1, Count = 1 }
                    };
                });

                return items.GetRange(0, 255 - segments).AddRange(entries);
            }

            return items;
        }*/

        public override LeveledItem ToMajorRecord()
        {
            var record = Base.DeepCopy();
            record.FormVersion = GetFormVersion();
            record.EditorID = GetEditorID();
            record.ChanceNone = GetChanceNone();
            record.Flags = GetFlags();
            record.Global.SetTo(GetGlobal());
            record.Entries = new(GetEntries());
            //record.Entries = GetEntries().ConvertAll(record => record.DeepCopy()).ToExtendedList();
            return record;
        }
    }
}
