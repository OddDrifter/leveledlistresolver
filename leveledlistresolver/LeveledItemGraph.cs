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
    public class LeveledItemGraph
    {
        public FormKey FormKey { get; }
        public ILeveledItemGetter Base { get; }
        public ImmutableHashSet<ModKey> ModKeys { get; }
        public ImmutableDictionary<ModKey, HashSet<ModKey>> Graph { get; }
        public ImmutableDictionary<ModKey, ILeveledItemGetter> Records { get; }
        public ImmutableHashSet<ILeveledItemGetter> ExtentRecords { get; }

        readonly ILinkCache<ISkyrimMod, ISkyrimModGetter> _linkCache;

        public LeveledItemGraph(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, FormKey formKey)
        {
            _linkCache = state.LinkCache;
            var loadOrder = state.LoadOrder.PriorityOrder.OnlyEnabledAndExisting();
            var mods = loadOrder.Resolve().Where(mod => mod.LeveledItems.ContainsKey(formKey));
            
            FormKey = formKey;
            Base = FormKey.AsLink<ILeveledItemGetter>().ResolveAll(_linkCache).Last();
            ModKeys = mods.Select(mod => mod.ModKey).ToImmutableHashSet();
            Graph = new[] { ModKey.Null }.Concat(ModKeys).ToImmutableDictionary(key => key, key => new HashSet<ModKey>());
            Records = mods.ToImmutableDictionary(mod => mod.ModKey, mod => mod.LeveledItems[FormKey]);

            foreach (var mod in mods)
            {
                var masterReferences = mod.ModHeader.MasterReferences;

                if (masterReferences.Count == 0)
                {
                    Graph[ModKey.Null].Add(mod.ModKey);
                }
                else
                {
                    var masters = masterReferences.Select(reference => reference.Master).Where(ModKeys.Contains).DefaultIfEmpty(ModKey.Null);
                    foreach (var key in masters)
                    {
                        Graph[key].Add(mod.ModKey);
                    }
                }
            }

            foreach (var (_, values) in Graph)
            {
                foreach (var value in values)
                {
                    var keys = Graph[value];
                    values.ExceptWith(keys.Intersect(values));
                }
            }

            ExtentRecords = Graph.Where(kvp => kvp.Value.Count == 0 || kvp.Value.Contains(state.PatchMod.ModKey)).Select(kvp => Records[kvp.Key]).ToImmutableHashSet();
            _= Traverse();
        }

        public List<List<ModKey>> Traverse() => Traverse(ModKey.Null);

        public List<List<ModKey>> Traverse(ModKey startingKey)
        {
            if (Graph.ContainsKey(startingKey) is false) 
                return new();

            List<List<ModKey>> paths = new();
            List<ModKey> path = new() { startingKey };
            HashSet<ModKey> extents = Graph.Keys.Where(key => Graph[key].Count == 0).ToHashSet();
            
            foreach (var extent in extents) 
                Visit(startingKey, extent, path);

            Console.WriteLine($"\n{GetEditorId()}");
            foreach (var value in paths)
                Console.WriteLine("Found Path: " + string.Join(" -> ", value));

            return paths;

            void Visit(ModKey startPoint, ModKey endPoint, IEnumerable<ModKey> path)
            {            
                if (startPoint == endPoint)
                {             
                    paths.Add(path.ToList());
                    return;
                }
                
                foreach (var node in Graph[startPoint])
                {
                    Visit(node, endPoint, path.Append(node));
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
            var added = entriesList.Skip(1).Aggregate(ImmutableList.CreateRange(entriesList.First().ExceptWith(baseEntries)), (list, items) => {
                var toAdd = items.ExceptWith(baseEntries).ExceptWith(list);
                return list.AddRange(toAdd);
            });

            var intersection = entriesList.Aggregate(ImmutableList.CreateRange(baseEntries), (list, items) => {
                return list.IntersectWith(items).ToImmutableList();
            });

            return added.Concat(intersection).Where(entry => entry.IsNullOrEmptySublist(_linkCache) is false);
        }

        public LeveledItem.Flag GetFlags()
        {
            var values = ExtentRecords.Select(record => record.Flags);
            return values.Where(flag => flag != Base.Flags).DefaultIfEmpty(Base.Flags).Last();
        }
    }
}
