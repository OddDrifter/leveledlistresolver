using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using System;
using System.Collections.Immutable;
using System.Linq;
using static MoreLinq.Extensions.BatchExtension;

namespace leveledlistresolver
{
    public class LeveledNpcGraph : MajorRecordGraphBase<ISkyrimMod, ISkyrimModGetter, ILeveledNpc, ILeveledNpcGetter>
    {
        public LeveledNpcGraph(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, in FormKey formKey) : base(state, formKey) { }

        public IObjectBoundsGetter GetObjectBounds()
        {
            return ExtentRecords.LastOrDefault(record => record.ObjectBounds != Base.ObjectBounds)?.ObjectBounds ?? Base.ObjectBounds;
        }

        public byte GetChanceNone()
        {
            return ExtentRecords.LastOrDefault(record => record.ChanceNone != Base.ChanceNone)?.ChanceNone ?? Base.ChanceNone;
        }

        public LeveledNpc.Flag GetFlags()
        {
            return ExtentRecords.LastOrDefault(record => record.Flags != Base.Flags)?.Flags ?? Base.Flags;
        }

        public IFormLinkGetter<IGlobalGetter> GetGlobal()
        {
            return ExtentRecords.LastOrDefault(record => record.Global != Base.Global)?.Global ?? Base.Global;
        }

        public ImmutableList<ILeveledNpcEntryGetter> GetEntries()
        {
            if (ExtentRecords.Count is 1)
                return ExtentRecords.Single().Entries?.ToImmutableList() ?? ImmutableList.Create<ILeveledNpcEntryGetter>();

            var baseEntries = ExtentBase?.Entries ?? Base.Entries ?? Array.Empty<ILeveledNpcEntryGetter>();
            var entriesList = ExtentRecords.Select(list => list.Entries ?? Array.Empty<ILeveledNpcEntryGetter>());

            var added = entriesList.Aggregate(ImmutableList.CreateBuilder<ILeveledNpcEntryGetter>(), (builder, items) => 
            {
                builder.AddRange(items.Without(baseEntries).Without(builder));
                return builder;
            }).ToImmutable();

            var intersection = entriesList.Aggregate(ImmutableList.CreateRange(baseEntries), static (list, items) =>
            {
                var toRemove = items.ToList();
                return list.FindAll(toRemove.Remove);
            });

            var items = added.AddRange(intersection).RemoveAll(entry => entry.IsNullOrEmptySublist(linkCache));

            if (items.Count > 255)
            {
                Console.WriteLine($"{GetEditorID()} had more than 255 items.");

                var segments = ((items.Count - 255) / 255) + 1;
                var extraItems = items.RemoveRange(0, 255 - segments);

                var entries = extraItems.Batch(255).WithIndex().Select((kvp) =>
                {
                    var leveledNpc = patchMod.LeveledNpcs.AddNew();
                    leveledNpc.EditorID = $"Mir_{GetEditorID()}_Sublist_{kvp.Index + 1}";
                    leveledNpc.Entries = kvp.Item.Select(r => r.DeepCopy()).ToExtendedList();
                    leveledNpc.Flags = GetFlags();
                    leveledNpc.Global.SetTo(GetGlobal());

                    return new LeveledNpcEntry()
                    {
                        Data = new() { Reference = leveledNpc.AsLink(), Level = 1, Count = 1 }
                    };
                });

                return items.GetRange(0, 255 - segments).AddRange(entries);
            }

            return items;
        }

        public IModelGetter? GetModel()
        {
            return ExtentRecords.LastOrDefault(record => record.Model != Base.Model)?.Model ?? Base.Model;
        }

        public override LeveledNpc ToMajorRecord()
        {
            var record = Base.DeepCopy();
            record.FormVersion = GetFormVersion();
            record.EditorID = GetEditorID();
            record.ChanceNone = GetChanceNone();
            record.Flags = GetFlags();
            record.Global.SetTo(GetGlobal());
            record.Entries = GetEntries().ConvertAll(record => record.DeepCopy()).ToExtendedList();
            record.Model = GetModel()?.DeepCopy();
            return record;
        }
    }
}
