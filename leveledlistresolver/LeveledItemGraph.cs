﻿using Mutagen.Bethesda;
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

        public LeveledItemGraph(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, FormKey formKey)
        {
            var linkCache = state.LinkCache;
            var loadOrder = state.LoadOrder.PriorityOrder.OnlyEnabled();
            var listings = loadOrder.Where(plugin => plugin.Mod is not null && plugin.Mod.LeveledItems.ContainsKey(formKey)).Reverse();
            
            FormKey = formKey;
            Base = formKey.AsLink<ILeveledItemGetter>().ResolveAll(linkCache).Last();
            ModKeys = listings.Select(plugin => plugin.ModKey).ToImmutableHashSet();
            Graph = new[] { ModKey.Null }.Concat(ModKeys).ToImmutableDictionary(key => key, key => new HashSet<ModKey>());
            Records = listings.ToImmutableDictionary(plugin => plugin.ModKey, plugin => plugin.Mod!.LeveledItems[FormKey]);

            foreach (var listing in listings)
            {
                if (listing.Mod is { } mod)
                {
                    var masterReferences = mod.ModHeader.MasterReferences;

                    if (masterReferences.Count == 0)
                    {
                        Graph[ModKey.Null].Add(mod.ModKey);
                    }
                    else
                    {
                        var masterKeys = masterReferences.Select(reference => reference.Master).Where(ModKeys.Contains).DefaultIfEmpty(ModKey.Null);
                        foreach (var key in masterKeys)
                        {
                            Graph[key].Add(mod.ModKey);
                        }
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

            ExtentRecords = Graph.Keys.Where(key => Graph[key].Count == 0).Select(key => Records[key]).ToImmutableHashSet();
            Traverse();
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

            return added.Concat(intersection);
        }

        public LeveledItem.Flag GetFlags()
        {
            var values = ExtentRecords.Select(record => record.Flags);
            return values.Where(flag => flag != Base.Flags).DefaultIfEmpty(Base.Flags).Last();
        }
    }
}