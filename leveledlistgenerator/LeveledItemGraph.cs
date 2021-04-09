using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace leveledlistgenerator
{
    public class LeveledItemGraph
    {
        Dictionary<ModKey, List<List<ModKey>>> foundPaths = new(); 

        FormKey FormKey { get; }
        ILeveledItemGetter Base { get; }
        IModListing<ISkyrimModGetter>[] Listings { get; }
        ImmutableDictionary<ModKey, IModListing<ISkyrimModGetter>> Mods { get; }
        ImmutableHashSet<ModKey> ModKeys { get; }
        Dictionary<ModKey, HashSet<ModKey>> Graph { get; }

        public LeveledItemGraph(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, FormKey formKey)
        {
            var linkCache = state.LinkCache;
            var loadOrder = state.LoadOrder.PriorityOrder.OnlyEnabled();
            var records = formKey.AsLink<ILeveledItemGetter>().ResolveAll(linkCache).Reverse();
            
            FormKey = formKey;
            Base = records.First();
            Listings = loadOrder.Where(mod => mod.Mod is not null && mod.Mod.LeveledItems.ContainsKey(formKey)).Reverse().ToArray();
            ModKeys = Listings.Select(mod => mod.ModKey).ToImmutableHashSet();
            Mods = Listings.ToImmutableDictionary(mod => mod.ModKey);
            Graph = new() { { ModKey.Null, new() } };

            foreach (var listing in Listings)
            {
                if (Graph.ContainsKey(listing.ModKey) is false)
                    Graph.Add(listing.ModKey, new());

                if (listing.Mod is { } mod)
                {
                    var masterReferences = mod.ModHeader.MasterReferences;
                    var masterKeys = masterReferences.Select(_ => _.Master).Where(ModKeys.Contains).DefaultIfEmpty(ModKey.Null);

                    foreach (var key in masterKeys)
                    {
                        Graph[key].Add(mod.ModKey);
                    }
                }
            }

            foreach (var (key, values) in Graph)
            {
                foreach (var value in values)
                {
                    var _ = Graph[value].ToHashSet();
                    _.IntersectWith(values);
                    values.ExceptWith(_);
                }
            }

            Traverse();
        }

        List<List<ModKey>> Traverse() => Traverse(ModKey.Null);

        List<List<ModKey>> Traverse(ModKey startingKey)
        {
            if (Graph.ContainsKey(startingKey) is false) 
                return new();

            if (foundPaths.ContainsKey(startingKey))
                return foundPaths[startingKey];

            List<List<ModKey>> paths = new();
            List<ModKey> path = new() { startingKey };
            HashSet<ModKey> endPoints = new();

            foreach (var (key, values) in Graph)
            {
                foreach (var value in values)
                {
                    if (Graph[value].Any() is false) 
                        endPoints.Add(value);
                }
            }
            
            foreach (var endPoint in endPoints) 
                Visit(startingKey, endPoint, path);

            foreach (var value in paths)
                Console.WriteLine("Found Path: " + string.Join(" -> ", value));

            foundPaths.Add(startingKey, paths);
            return paths;

            void Visit(ModKey startPoint, ModKey endPoint, List<ModKey> path)
            {            
                if (startPoint == endPoint)
                {             
                    paths.Add(path.ToList());
                    return;
                }
                
                foreach (var node in Graph[startPoint])
                {
                    path.Add(node);
                    Visit(node, endPoint, path);
                    path.RemoveAt(path.Count - 1);
                }
            }
        }

        public string GetEditorId()
        {
            var paths = Traverse();
            var records = paths.Select(path => path.Last()).Distinct()
                .Select(key => Mods[key].Mod!.LeveledItems[FormKey])
                .Select(record => record.EditorID);

            return records.Where(id => id is not null && !id.Equals(Base.EditorID, StringComparison.InvariantCulture)).LastOrDefault() ?? (Base.EditorID ?? Guid.NewGuid().ToString());
        }

        public byte GetChanceNone()
        {
            var paths = Traverse();
            var records = paths.Select(path => path.Last()).Distinct()
                .Select(key => Mods[key].Mod!.LeveledItems[FormKey])
                .Select(record => record.ChanceNone);

            return records.Where(chanceNone => chanceNone != Base.ChanceNone).DefaultIfEmpty(Base.ChanceNone).Last();
        }

        public IFormLinkNullable<IGlobalGetter> GetGlobal()
        {
            var paths = Traverse();
            var records = paths.Select(path => path.Last()).Distinct()
                .Select(key => Mods[key].Mod!.LeveledItems[FormKey])
                .Select(record => record.Global);

            return records.Where(global => global != Base.Global).DefaultIfEmpty(Base.Global).Last().AsNullable();
        }

        public IEnumerable<ILeveledItemEntryGetter> GetEntries()
        {
            var paths = Traverse();
            var keys = paths.Select(path => path.Last()).Distinct();
            var records = keys.Select(key => Mods[key].Mod!.LeveledItems[FormKey]);

            if (records.Count() == 1)
                return records.First().Entries.EmptyIfNull();

            var baseEntries = Base.Entries.EmptyIfNull();

            List<ILeveledItemEntryGetter> itemsAdded = new();
            ImmutableList<ILeveledItemEntryGetter> itemIntersection = records.SelectMany(record => record.Entries.EmptyIfNull()).ToImmutableList();
            ImmutableList<ILeveledItemEntryGetter> addedIntersection = records.SelectMany(record => record.Entries.EmptyIfNull().ExceptWith(baseEntries)).ToImmutableList();

            foreach (var record in records)
            {
                var entries = record.Entries.EmptyIfNull();
                itemIntersection = entries.IntersectWith(itemIntersection).ToImmutableList();
                addedIntersection = entries.ExceptWith(baseEntries).IntersectWith(addedIntersection).ToImmutableList();
            }

            foreach (var record in records)
            {
                var entries = record.Entries.EmptyIfNull().ExceptWith(baseEntries).ExceptWith(addedIntersection);
                itemsAdded.AddRange(entries);
            }

            //Todo: Fix Similar Paths (Maybe not worthwhile)
            return itemsAdded.And(itemIntersection);
        }

        public LeveledItem.Flag GetFlags()
        {
            var paths = Traverse();
            var records = paths.Select(path => path.Last()).Distinct()
                .Select(key => Mods[key].Mod!.LeveledItems[FormKey])
                .Select(record => record.Flags);

            return records.Where(flag => flag != Base.Flags).DefaultIfEmpty(Base.Flags).Last();
        }

        void Print()
        {
            foreach (var (key, values) in Graph) Console.WriteLine("{0}: {{ {1} }}", key, string.Join(", ", values.ToArray()));
        }
    }
}
