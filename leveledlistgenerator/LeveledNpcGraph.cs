using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace leveledlistgenerator
{
    public class LeveledNpcGraph
    {
        Dictionary<ModKey, List<List<ModKey>>> foundPaths = new();

        FormKey FormKey { get; }
        ILeveledNpcGetter Base { get; }
        ImmutableHashSet<ModKey> ModKeys { get; }
        Dictionary<ModKey, HashSet<ModKey>> Graph { get; }
        ImmutableDictionary<ModKey, ILeveledNpcGetter> Records { get; }
        ImmutableHashSet<ILeveledNpcGetter> ExtentRecords { get; }

        public LeveledNpcGraph(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, FormKey formKey)
        {
            var linkCache = state.LinkCache;
            var loadOrder = state.LoadOrder.PriorityOrder.OnlyEnabled();
            var listings = loadOrder.Where(plugin => plugin.Mod is not null && plugin.Mod.LeveledNpcs.ContainsKey(formKey)).Reverse();
            var recordBuilder = ImmutableDictionary.CreateBuilder<ModKey, ILeveledNpcGetter>();

            FormKey = formKey;
            Base = formKey.AsLink<ILeveledNpcGetter>().ResolveAll(linkCache).Last();
            ModKeys = listings.Select(plugin => plugin.ModKey).ToImmutableHashSet();
            Graph = new() { { ModKey.Null, new() } };           

            foreach (var listing in listings)
            {
                if (Graph.ContainsKey(listing.ModKey) is false)
                    Graph.Add(listing.ModKey, new());

                if (listing.Mod is { } mod)
                {
                    var masterReferences = mod.ModHeader.MasterReferences;
                    if (masterReferences.Count == 0)
                    {
                        Graph[ModKey.Null].Add(mod.ModKey);
                    }
                    else
                    {
                        var masterKeys = masterReferences.Select(_ => _.Master).Where(ModKeys.Contains).DefaultIfEmpty(ModKey.Null);
                        foreach (var key in masterKeys)
                        {
                            Graph[key].Add(mod.ModKey);
                        }
                    }
                    recordBuilder.Add(mod.ModKey, mod.LeveledNpcs[FormKey]);
                }
            }

            Records = recordBuilder.ToImmutable();

            var extentRecordBuilder = ImmutableHashSet.CreateBuilder<ILeveledNpcGetter>();

            foreach (var (_, values) in Graph)
            {
                foreach (var value in values)
                {
                    var _ = Graph[value];
                    if (_.Count == 0)
                        extentRecordBuilder.Add(Records[value]);
                    values.ExceptWith(_.Intersect(values));
                }
            }

            ExtentRecords = extentRecordBuilder.ToImmutable();
            Traverse();
        }

        public List<List<ModKey>> Traverse() => Traverse(ModKey.Null);

        public List<List<ModKey>> Traverse(ModKey startingKey)
        {
            if (Graph.ContainsKey(startingKey) is false)
                return new();

            if (foundPaths.ContainsKey(startingKey))
                return foundPaths[startingKey];

            List<List<ModKey>> paths = new();
            List<ModKey> path = new() { startingKey };
            HashSet<ModKey> endPoints = new();

            foreach (var (_, values) in Graph)
            {
                foreach (var value in values)
                {
                    if (Graph[value].Count == 0)
                        endPoints.Add(value);
                }
            }

            foreach (var endPoint in endPoints)
                Visit(startingKey, endPoint, path);

            foreach (var value in paths)
                Console.WriteLine("Found Path: " + string.Join(" -> ", value));

            foundPaths.Add(startingKey, paths);
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

        public IEnumerable<ILeveledNpcEntryGetter> GetEntries()
        {
            if (ExtentRecords.Count == 1)
                return ExtentRecords.First().Entries ?? Array.Empty<ILeveledNpcEntryGetter>();

            var baseEntries = Base.Entries ?? Array.Empty<ILeveledNpcEntryGetter>();
            var entriesList = ExtentRecords.Select(list => list.Entries ?? Array.Empty<ILeveledNpcEntryGetter>());

            var added = entriesList.Skip(1).Aggregate(ImmutableList.CreateRange(entriesList.First().ExceptWith(baseEntries)), (list, items) => {
                var toAdd = items.ExceptWith(baseEntries).ExceptWith(list);
                return list.AddRange(toAdd);
            });

            var intersection = entriesList.Aggregate(ImmutableList.CreateRange(baseEntries), (list, items) => {
                return list.IntersectWith(items).ToImmutableList();
            });

            return added.Concat(intersection);
        }

        public LeveledNpc.Flag GetFlags()
        {
            var values = ExtentRecords.Select(record => record.Flags);
            return values.Where(flag => flag != Base.Flags).DefaultIfEmpty(Base.Flags).Last();
        }
    }
}
