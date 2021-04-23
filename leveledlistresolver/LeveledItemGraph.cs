using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static MoreLinq.Extensions.BatchExtension;

namespace leveledlistresolver
{
    public class LeveledItemGraph
    {
        public FormKey FormKey { get => Base?.FormKey ?? FormKey.Null; }
        public ILeveledItemGetter Base { get; }
        public ImmutableHashSet<ModKey> ModKeys { get; }
        public ImmutableDictionary<ModKey, HashSet<ModKey>> Graph { get; }
        public ImmutableDictionary<ModKey, ILeveledItemGetter> Records { get; }
        public ImmutableHashSet<ILeveledItemGetter> ExtentRecords { get; }

        readonly ISkyrimMod _patchMod;
        readonly ILinkCache<ISkyrimMod, ISkyrimModGetter> _linkCache;

        public LeveledItemGraph(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, FormKey formKey)
        {
            _patchMod = state.PatchMod;
            _linkCache = state.LinkCache;

            var contexts = _linkCache.ResolveAllContexts<ILeveledItem, ILeveledItemGetter>(formKey);

            Base = contexts.Last().Record;
            Records = contexts.ToImmutableDictionary(context => context.ModKey, context => context.Record);
            ModKeys = Records.Keys.ToImmutableHashSet();
            Graph = ModKeys.Add(ModKey.Null).ToImmutableDictionary(key => key, key => new HashSet<ModKey>());

            var mods = state.LoadOrder.PriorityOrder.OnlyEnabledAndExisting().Resolve().Where(mod => ModKeys.Contains(mod.ModKey));

            foreach (var mod in mods)
            {
                var masters = mod.ModHeader.MasterReferences
                    .Select(reference => reference.Master)
                    .Where(ModKeys.Contains)
                    .DefaultIfEmpty(ModKey.Null);

                foreach (var master in masters)
                {
                    Graph[master].Add(mod.ModKey);
                }
            }

            foreach (var values in Graph.Values)
            {
                foreach (var value in values)
                {
                    var keys = Graph[value];
                    values.ExceptWith(keys.Intersect(values));
                }
            }

            ExtentRecords = Graph.Where(kvp => kvp.Value.Count == 0 || kvp.Value.Contains(_patchMod.ModKey)).Select(kvp => Records[kvp.Key]).ToImmutableHashSet();
            _= Traverse();
        }

        public List<ImmutableList<ModKey>> Traverse(ModKey? startingKey = null)
        {
            var startPoint = startingKey ?? ModKey.Null;

            if (Graph.ContainsKey(startPoint) is false) 
                return new();

            List<ImmutableList<ModKey>> paths = new();

            var path = startPoint.IsNull ? ImmutableList.Create<ModKey>() : ImmutableList.Create(startPoint);
            var extents = Graph.Where(kvp => kvp.Value.Count is 0);
            
            foreach (var (extent, _) in extents) 
                Visit(startPoint, extent, path);

            Console.WriteLine($"\n{GetEditorId()}");
            foreach (var value in paths)
                Console.WriteLine("Found Path: " + string.Join(" -> ", value));

            return paths;

            void Visit(ModKey startPoint, ModKey endPoint, ImmutableList<ModKey> path)
            {            
                if (startPoint == endPoint)
                {             
                    paths.Add(path);
                    return;
                }
                
                foreach (var node in Graph[startPoint])
                {
                    Visit(node, endPoint, path.Add(node));
                }
            }
        }

        public string GetEditorId()
        {
            var values = ExtentRecords.Select(record => record.EditorID);
            return values.Where(id => id is not null && !id.Equals(Base.EditorID, StringComparison.InvariantCulture)).LastOrDefault() ?? Base.EditorID ?? Guid.NewGuid().ToString();
        }

        public byte GetChanceNone()
        {
            var values = ExtentRecords.Select(record => record.ChanceNone);
            return values.Where(chanceNone => chanceNone != Base.ChanceNone).DefaultIfEmpty(Base.ChanceNone).Last();
        }

        public IFormLinkNullable<IGlobalGetter> GetGlobal()
        {
            var values = ExtentRecords.Select(record => record.Global);
            return values.Where(global => global != Base.Global).DefaultIfEmpty(Base.Global).Last().AsNullable();
        }

        public IEnumerable<ILeveledItemEntryGetter> GetEntries()
        {
            if (ExtentRecords.Count == 1)
                return ExtentRecords.First().Entries ?? Array.Empty<ILeveledItemEntryGetter>();

            var baseEntries = Base.Entries ?? Array.Empty<ILeveledItemEntryGetter>();
            var entriesList = ExtentRecords.Select(list => list.Entries ?? Array.Empty<ILeveledItemEntryGetter>());

            var added = entriesList.Aggregate(ImmutableList.CreateBuilder<ILeveledItemEntryGetter>(), (builder, items) =>
            {
                builder.AddRange(items.ExceptWith(baseEntries).ExceptWith(builder));
                return builder;
            }).ToImmutable();

            var intersection = entriesList.Aggregate(ImmutableList.CreateRange(baseEntries), (list, items) =>
            {
                var toRemove = items.ToList();
                return list.FindAll(toRemove.Remove);
            });

            var items = added.AddRange(intersection).RemoveAll(entry => entry.IsNullOrEmptySublist(_linkCache));

            if (items.Count > 255)
            {
                Console.WriteLine($"{GetEditorId()} had more than 255 items.");

                var segments = ((items.Count - 255) / 255) + 1;
                var extraItems = items.RemoveRange(0, 255 - segments);
                
                var entries = extraItems.Batch(255).WithIndex().Select((kvp) => 
                {
                    var leveledItem = _patchMod.LeveledItems.AddNew();
                    leveledItem.EditorID = $"Mir_{GetEditorId()}_Sublist_{kvp.Index + 1}";
                    leveledItem.Entries = kvp.Item.Select(r => r.DeepCopy()).ToExtendedList();
                    leveledItem.Flags = GetFlags();
                    leveledItem.Global = GetGlobal();

                    return new LeveledItemEntry()
                    {
                        Data = new() { Reference = leveledItem.AsLink(), Level = 1, Count = 1 }
                    };
                });

                return items.GetRange(0, 255 - segments).AddRange(entries);
            }
            return items;
        }

        public LeveledItem.Flag GetFlags()
        {
            var values = ExtentRecords.Select(record => record.Flags);
            return values.Where(flag => flag != Base.Flags).DefaultIfEmpty(Base.Flags).Last();
        }
    }
}
