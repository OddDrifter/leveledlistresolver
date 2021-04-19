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
    public class LeveledNpcGraph
    {
        public FormKey FormKey { get => Base?.FormKey ?? FormKey.Null; }
        public ILeveledNpcGetter Base { get; }
        public ImmutableHashSet<ModKey> ModKeys { get; }
        public ImmutableDictionary<ModKey, HashSet<ModKey>> Graph { get; }
        public ImmutableDictionary<ModKey, ILeveledNpcGetter> Records { get; }
        public ImmutableHashSet<ILeveledNpcGetter> ExtentRecords { get; }

        readonly ILinkCache<ISkyrimMod, ISkyrimModGetter> _linkCache;

        public LeveledNpcGraph(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, FormKey formKey)
        {
            _linkCache = state.LinkCache;

            var contexts = _linkCache.ResolveAllContexts<ILeveledNpc, ILeveledNpcGetter>(formKey);

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

            ExtentRecords = Graph.Keys.Where(key => Graph[key].Count == 0 || Graph[key].Contains(state.PatchMod.ModKey)).Select(key => Records[key]).ToImmutableHashSet();
            _ = Traverse();
        }

        public List<ImmutableArray<ModKey>> Traverse() => Traverse(ModKey.Null);

        public List<ImmutableArray<ModKey>> Traverse(ModKey startingKey)
        {
            if (Graph.ContainsKey(startingKey) is false)
                return new();

            List<ImmutableArray<ModKey>> paths = new();
            var extents = ModKeys.Where(key => Graph[key].Count is 0);

            foreach (var extent in extents)
                Visit(startingKey, extent, ImmutableArray.Create(startingKey));

            Console.WriteLine($"\n{GetEditorId()}");
            foreach (var value in paths)
                Console.WriteLine("Found Path: " + string.Join(" -> ", value));

            return paths;

            void Visit(ModKey startPoint, ModKey endPoint, ImmutableArray<ModKey> path)
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

        public IEnumerable<ILeveledNpcEntryGetter> GetEntries()
        {
            if (ExtentRecords.Count == 1)
                return ExtentRecords.First().Entries ?? Array.Empty<ILeveledNpcEntryGetter>();

            var baseEntries = Base.Entries ?? Array.Empty<ILeveledNpcEntryGetter>();
            var entriesList = ExtentRecords.Select(list => list.Entries ?? Array.Empty<ILeveledNpcEntryGetter>());

            var added = entriesList.Aggregate(ImmutableList.CreateBuilder<ILeveledNpcEntryGetter>(), (builder, items) => 
            {
                builder.AddRange(items.ExceptWith(baseEntries).ExceptWith(builder));
                return builder;
            });

            var intersection = entriesList.Aggregate(ImmutableList.CreateRange(baseEntries), (list, items) => 
            {
                return list.IntersectWith(items).ToImmutableList();
            });

            if (added.Count > 255)
                Console.WriteLine($"{GetEditorId()} had more than 255 items.");

            added.AddRange(intersection);
            return added.ToImmutable().Where(entry => entry.IsNullOrEmptySublist(_linkCache) is false);
        }

        public LeveledNpc.Flag GetFlags()
        {
            var values = ExtentRecords.Select(record => record.Flags);
            return values.Where(flag => flag != Base.Flags).DefaultIfEmpty(Base.Flags).Last();
        }
    }
}
