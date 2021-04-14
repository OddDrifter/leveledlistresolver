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

namespace leveledlistresolver
{
    static class Patcher
    {
        static readonly LeveledItem.TranslationMask LeveledItemMask = new(defaultOn: true) { 
            FormVersion = false, VersionControl = false, Version2 = false, Entries = false 
        };

        static readonly LeveledNpc.TranslationMask LeveledNpcMask = new (defaultOn: true) { 
            FormVersion = false, VersionControl = false, Version2 = false, Entries = false 
        };

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
            var leveledItemsToOverride = FindRecordsToOverride(state.LinkCache, leveledItems.ToArray());

            Console.WriteLine($"Found {leveledItemsToOverride.Count()} LeveledItem(s) to override");

            foreach (var leveledItem in leveledItemsToOverride)
            {
                var graph = new LeveledItemGraph(state, leveledItem.FormKey);
                var copy = graph.Base.DeepCopy();

                //Todo: Actually handle there being more than 255 items.
                copy.FormVersion = 44;
                copy.EditorID = graph.GetEditorId();
                copy.Entries = graph.GetEntries().Select(record => record.DeepCopy()).Take(255).ToExtendedList();
                copy.ChanceNone = graph.GetChanceNone();
                copy.Global = graph.GetGlobal();
                copy.Flags = graph.GetFlags();

                if (copy.Equals(leveledItem, LeveledItemMask) && copy.Entries.IntersectWith(leveledItem.Entries.EmptyIfNull()).CompareCount(copy.Entries) == 0)
                {
                    Console.WriteLine($"Skipping [{copy.FormKey}] {copy.EditorID}");
                    continue;
                }

                state.PatchMod.LeveledItems.Set(copy);
            }

            var leveledNpcs = loadOrder.PriorityOrder.OnlyEnabled().WinningOverrides<ILeveledNpcGetter>();            
            var leveledNpcsToOverride = FindRecordsToOverride(state.LinkCache, leveledNpcs.ToArray());

            Console.WriteLine($"\nFound {leveledNpcsToOverride.Count()} LeveledNpc(s) to override");

            foreach (var leveledNpc in leveledNpcsToOverride)
            {
                var graph = new LeveledNpcGraph(state, leveledNpc.FormKey);
                var copy = graph.Base.DeepCopy();

                //Todo: Actually handle there being more than 255 items.
                copy.FormVersion = 44;
                copy.EditorID = graph.GetEditorId();
                copy.ChanceNone = graph.GetChanceNone();
                copy.Entries = graph.GetEntries().Select(r => r.DeepCopy()).Take(255).ToExtendedList();
                copy.Global = graph.GetGlobal();
                copy.Flags = graph.GetFlags();

                if (copy.Equals(leveledNpc, LeveledNpcMask) && copy.Entries.IntersectWith(leveledNpc.Entries.EmptyIfNull()).CompareCount(copy.Entries) == 0)
                {
                    Console.WriteLine($"Skipping [{copy.FormKey}] {copy.EditorID}");
                    continue;
                }

                state.PatchMod.LeveledNpcs.Set(copy);
            }

            /*var leveledSpells = loadOrder.PriorityOrder.OnlyEnabled().WinningOverrides<ILeveledSpellGetter>()
                .TryFindOverrides<ILeveledSpellGetter, ILeveledSpellEntryGetter>(state.LinkCache);*/

            Console.WriteLine("\nReport any issues at https://github.com/OddDrifter/leveledlistgenerator/issues \n");
        }

        private static IEnumerable<ILeveledItemGetter> FindRecordsToOverride(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, params ILeveledItemGetter[] getters)
        {
            foreach (var getter in getters)
            {
                //Todo: Should also check EditorID, Flags, and Global
                var records = getter.AsLink().ResolveAll(linkCache).Reverse().ToArray();

                if (records.Length <= 1)
                    continue;

                if (records[1..].Window(2).Any(window => window[0].Entries?.ToImmutableHashSet().IsSubsetOf(window[^1].Entries.EmptyIfNull()) is false))
                {
                    yield return records[^1];
                }
            }
        }

        private static IEnumerable<ILeveledNpcGetter> FindRecordsToOverride(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, params ILeveledNpcGetter[] getters)
        {
            foreach (var getter in getters)
            {
                //Todo: Should also check EditorID, Flags, and Global
                var records = getter.AsLink().ResolveAll(linkCache).Reverse().ToArray();

                if (records.Length <= 1) 
                    continue;

                if (records[1..].Window(2).Any(window => window[0].Entries?.ToImmutableHashSet().IsSubsetOf(window[^1].Entries.EmptyIfNull()) is false))
                {
                    yield return records[^1];
                }
            }
        }
    }
}