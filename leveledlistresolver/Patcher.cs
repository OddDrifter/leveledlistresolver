using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using System;
using System.Linq;
using System.Threading.Tasks;

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

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .SetTypicalOpen(GameRelease.SkyrimSE, "Leveled Lists.esp")
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(Apply)
                .Run(args);
        }

        private static void Apply(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            using var loadOrder = state.LoadOrder;

            var leveledItems = loadOrder.PriorityOrder.OnlyEnabled().WinningOverrides<ILeveledItemGetter>()
                .Where(form => Utility.CountExtents<ILeveledItem, ILeveledItemGetter>(state, form.FormKey) > 1);

            Console.WriteLine($"Found {leveledItems.Count()} LeveledItem(s) to override");

            foreach (var leveledItem in leveledItems)
            {
                var graph = new LeveledItemGraph(state, leveledItem.FormKey);
                var copy = graph.Base.DeepCopy();

                copy.FormVersion = 44;
                copy.EditorID = graph.GetEditorId();
                copy.Entries = graph.GetEntries().Select(record => record.DeepCopy()).ToExtendedList();
                copy.ChanceNone = graph.GetChanceNone();
                copy.Global = graph.GetGlobal();
                copy.Flags = graph.GetFlags();

                if (copy.Equals(leveledItem, LeveledItemMask) && Utility.UnsortedEqual(copy.Entries, leveledItem.Entries ?? Array.Empty<ILeveledItemEntryGetter>()))
                {
                    Console.WriteLine($"Skipping [{copy.FormKey}] {copy.EditorID}");
                    continue;
                }

                state.PatchMod.LeveledItems.Set(copy);
            }

            var leveledNpcs = loadOrder.PriorityOrder.OnlyEnabled().WinningOverrides<ILeveledNpcGetter>()
                .Where(form => Utility.CountExtents<ILeveledNpc, ILeveledNpcGetter>(state, form.FormKey) > 1);

            Console.WriteLine($"\nFound {leveledNpcs.Count()} LeveledNpc(s) to override");

            foreach (var leveledNpc in leveledNpcs)
            {
                var graph = new LeveledNpcGraph(state, leveledNpc.FormKey);
                var copy = graph.Base.DeepCopy();

                copy.FormVersion = 44;
                copy.EditorID = graph.GetEditorId();
                copy.ChanceNone = graph.GetChanceNone();
                copy.Entries = graph.GetEntries().Select(r => r.DeepCopy()).ToExtendedList();
                copy.Global = graph.GetGlobal();
                copy.Flags = graph.GetFlags();

                if (copy.Equals(leveledNpc, LeveledNpcMask) && Utility.UnsortedEqual(copy.Entries, leveledNpc.Entries ?? Array.Empty<ILeveledNpcEntryGetter>()))
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
    }
}