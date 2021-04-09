using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using static MoreLinq.Extensions.WindowExtension;
using static MoreLinq.Extensions.CompareCountExtension;

namespace leveledlistgenerator
{
    static class Patcher
    {
        public static async Task Main(string[] args)
        {
            await SynthesisPipeline.Instance
                .SetTypicalOpen(GameRelease.SkyrimSE, "Leveled Lists.esp")
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(Apply)
                .Run(args);
        }

        private static void Apply(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            using var loadOrder = state.LoadOrder;

            var leveledItems = loadOrder.PriorityOrder.OnlyEnabled().WinningOverrides<ILeveledItemGetter>();
            var leveledItemsMask = new LeveledItem.TranslationMask(defaultOn: true) 
                { FormVersion = false, VersionControl = false, Version2 = false, Entries = false };
            var leveledItemsToOverride = FindRecordsToOverride(state.LinkCache, leveledItems.ToArray());

            Console.WriteLine($"Found {leveledItemsToOverride.Count()} LeveledItem(s) to override");

            foreach (var (baseRecord, overrides) in leveledItemsToOverride)
            {
                Console.WriteLine("\n" + baseRecord.EditorID);

                var copy = baseRecord.DeepCopy();
                var graph = new LeveledItemGraph(state, copy.FormKey);

                copy.FormVersion = 44;
                copy.EditorID = graph.GetEditorId();
                //Todo: Actually handle there being more than 255 items.
                copy.Entries = graph.GetEntries().Select(record => record.DeepCopy()).Take(255).ToExtendedList();
                copy.ChanceNone = graph.GetChanceNone();
                copy.Global = graph.GetGlobal();
                copy.Flags = graph.GetFlags();

                var itemsRemoved = copy.Entries.RemoveAll(entry => IsNullOrEmptySublist(entry, state.LinkCache));

                if (itemsRemoved == 0 && copy.Equals(overrides.Last(), leveledItemsMask) && 
                    copy.Entries.IntersectWith(overrides.Last().Entries.EmptyIfNull()).CompareCount(copy.Entries) == 0)
                {
                    Console.WriteLine($"Skipping [{copy.FormKey}] {copy.EditorID}");
                    continue;
                }

                state.PatchMod.LeveledItems.Set(copy);
            }

            /*var leveledNpcs = loadOrder.PriorityOrder.OnlyEnabled().WinningOverrides<ILeveledNpcGetter>();
            var leveledNpcsMask = new LeveledNpc.TranslationMask(defaultOn: true)
                { FormVersion = false, VersionControl = false, Version2 = false, Entries = false };
            var leveledNpcsToOverride = FindRecordsToOverride(state.LinkCache, leveledNpcs.ToArray());

            Console.WriteLine($"\nFound {leveledNpcsToOverride.Count()} LeveledNpc(s) to override");

            foreach (var (baseRecord, overrides) in leveledNpcsToOverride)
            {
                var copy = baseRecord.DeepCopy(leveledNpcsMask);

                copy.FormVersion = 44;
                copy.EditorID = FindEditorID(baseRecord, overrides);
                copy.ChanceNone = FindChanceNone(baseRecord.ChanceNone, overrides, record => record.ChanceNone);
                copy.Entries = FindEntries(baseRecord, overrides, record => record.Entries).Select(r => r.DeepCopy()).ToExtendedList();
                copy.Global = overrides.Where(record => record.Global != baseRecord.Global).DefaultIfEmpty(baseRecord).Last().Global.AsNullable();

                foreach (var leveledNpc in overrides.Where(record => record.Flags != baseRecord.Flags))
                {
                    foreach (var flag in Enum.GetValues<LeveledNpc.Flag>())
                    {
                        if ((leveledNpc.Flags & flag) == flag) 
                            copy.Flags |= flag;

                        if ((~leveledNpc.Flags & baseRecord.Flags & flag) == flag) 
                            copy.Flags &= ~flag;
                    }
                }

                var itemsRemoved = copy.Entries.RemoveAll(entry => IsNullOrEmptySublist(entry, state.LinkCache));

                if (itemsRemoved == 0 && copy.Equals(overrides.Last(), leveledNpcsMask) &&
                    copy.Entries.IntersectWith(overrides.Last().Entries.EmptyIfNull()).CompareCount(copy.Entries) == 0)
                {
                    Console.WriteLine($"Skipping [{copy.FormKey}] {copy.EditorID}");
                    continue;
                }

                state.PatchMod.LeveledNpcs.Set(copy);
            }*/

            /*var leveledSpells = loadOrder.PriorityOrder.OnlyEnabled().WinningOverrides<ILeveledSpellGetter>()
                .TryFindOverrides<ILeveledSpellGetter, ILeveledSpellEntryGetter>(state.LinkCache);*/

            Console.WriteLine("\nReport any issues at https://github.com/OddDrifter/leveledlistgenerator/issues \n");
        }

        private static IEnumerable<(ILeveledItemGetter, IEnumerable<ILeveledItemGetter>)> FindRecordsToOverride(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, params ILeveledItemGetter[] getters)
        {
            foreach (var getter in getters)
            {
                //Todo: Should also check EditorID, Flags, and Global
                //Todo: Use Graph Instead
                var records = getter.AsLink().ResolveAll(linkCache).Reverse().ToArray();

                if (records.Length <= 1)
                    continue;

                if (records[1..].Window(2).Any(window => window[0].Entries?.ToImmutableHashSet().IsSubsetOf(window[^1].Entries.EmptyIfNull()) is false))
                {
                    yield return (records[0], records);
                }
            }
        }

        private static IEnumerable<(ILeveledNpcGetter, IEnumerable<ILeveledNpcGetter>)> FindRecordsToOverride(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, params ILeveledNpcGetter[] getters)
        {
            foreach (var getter in getters)
            {
                //Todo: Should also check EditorID, Flags, and Global
                var records = getter.AsLink().ResolveAll(linkCache).Reverse().ToArray();

                if (records.Length <= 1) 
                    continue;

                if (records[1..].Window(2).Any(window => window[0].Entries?.ToImmutableHashSet().IsSubsetOf(window[^1].Entries.EmptyIfNull()) is false))
                {
                    yield return (records[0], records);
                }
            }
        }

        private static bool IsNullOrEmptySublist(this ILeveledItemEntryGetter entry, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            if (entry.Data == null || entry.Data.Reference.IsNull) 
                return true;

            if (entry.Data.Reference.TryResolve(linkCache, out var itemGetter) && itemGetter is ILeveledItemGetter leveledItem)
                return leveledItem.Entries is null || leveledItem.Entries.Any() is false;

            return false;
        }

        private static bool IsNullOrEmptySublist(this ILeveledNpcEntryGetter entry, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            if (entry.Data == null || entry.Data.Reference.IsNull)
                return true;

            if (entry.Data.Reference.TryResolve(linkCache, out var npcSpawnGetter) && npcSpawnGetter is ILeveledNpcGetter leveledNpc)
                return leveledNpc.Entries is null || leveledNpc.Entries.Any() is false;

            return false;
        }
    }
}