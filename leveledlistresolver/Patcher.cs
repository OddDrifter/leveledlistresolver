using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using System;
using System.Collections.Immutable;
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

        static readonly LeveledSpell.TranslationMask LeveledSpellMask = new(defaultOn: true) {
            FormVersion = false, VersionControl = false, Version2 = false, Entries = false
        };

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .SetTypicalOpen(GameRelease.SkyrimSE, "Leveled Lists.esp")
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(Apply)
                .Run(args);
        }

        public static void Apply(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            Console.WriteLine("Report issues at https://github.com/OddDrifter/leveledlistresolver/issues");

            using var loadOrder = state.LoadOrder;
            var enabledAndExisting = loadOrder.PriorityOrder.OnlyEnabledAndExisting().ToImmutableArray();

            var leveledItems = enabledAndExisting.WinningOverrides<ILeveledItemGetter>()
                .Where(form => Utility.CountExtents<ILeveledItem, ILeveledItemGetter>(state, form.FormKey) > 1)
                .ToImmutableArray();

            if (leveledItems.IsEmpty is false)
            {
                Console.WriteLine($"Found {leveledItems.Length} LeveledItem(s) to override");

                foreach (var leveledItem in leveledItems)
                {
                    var copy = new LeveledItemGraph(state, leveledItem.FormKey).ToMajorRecord();

                    if (copy.Equals(leveledItem, LeveledItemMask) && Utility.UnsortedEqual(copy.Entries!, leveledItem.Entries ?? Array.Empty<ILeveledItemEntryGetter>()))
                    {
                        Console.WriteLine($"Skipping {copy.EditorID}");
                        continue;
                    }

                    state.PatchMod.LeveledItems.Set(copy);
                }
            }

            var leveledNpcs = enabledAndExisting.WinningOverrides<ILeveledNpcGetter>()
                .Where(form => Utility.CountExtents<ILeveledNpc, ILeveledNpcGetter>(state, form.FormKey) > 1)
                .ToImmutableArray();

            if (leveledNpcs.IsEmpty is false)
            {
                Console.WriteLine($"{Environment.NewLine}Found {leveledNpcs.Length} LeveledNpc(s) to override");

                foreach (var leveledNpc in leveledNpcs)
                {
                    var copy = new LeveledNpcGraph(state, leveledNpc.FormKey).ToMajorRecord();

                    if (copy.Equals(leveledNpc, LeveledNpcMask) && Utility.UnsortedEqual(copy.Entries!, leveledNpc.Entries ?? Array.Empty<ILeveledNpcEntryGetter>()))
                    {
                        Console.WriteLine($"Skipping {copy.EditorID}");
                        continue;
                    }

                    state.PatchMod.LeveledNpcs.Set(copy);
                }
            }

            var leveledSpells = enabledAndExisting.WinningOverrides<ILeveledSpellGetter>()
                .Where(form => Utility.CountExtents<ILeveledSpell, ILeveledSpellGetter>(state, form.FormKey) > 1)
                .ToImmutableArray();

            if (leveledSpells.IsEmpty is false)
            {
                Console.WriteLine($"{Environment.NewLine}Found {leveledSpells.Length} LeveledSpell(s) to override");

                foreach (var leveledSpell in leveledSpells)
                {
                    var copy = new LeveledSpellGraph(state, leveledSpell.FormKey).ToMajorRecord();

                    if (copy.Equals(leveledSpell, LeveledSpellMask) && Utility.UnsortedEqual(copy.Entries!, leveledSpell.Entries ?? Array.Empty<ILeveledSpellEntryGetter>()))
                    {
                        Console.WriteLine($"Skipping {copy.EditorID}");
                        continue;
                    }
                    
                    state.PatchMod.LeveledSpells.Set(copy);
                }
            }
        }      
    }
}
