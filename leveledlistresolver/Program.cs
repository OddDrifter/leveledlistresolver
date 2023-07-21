using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace leveledlistresolver
{
    class Program
    {
        private static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance.SetTypicalOpen(GameRelease.SkyrimSE, new("Leveled Lists.esp", ModType.LightMaster))
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(Apply)
                .Run(args);
        }

        public static void Apply(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            Console.WriteLine("Report issues at https://github.com/OddDrifter/leveledlistresolver/issues");

            using var loadOrder = state.LoadOrder;

            var linkCache = state.LinkCache;
            var enabledAndExisting = loadOrder.PriorityOrder.OnlyEnabledAndExisting().ToImmutableArray();
            var comparer = ModKey.LoadOrderComparer(ImmutableArray.CreateRange(state.LoadOrder.Keys));

            foreach (var leveledItem in enabledAndExisting.LeveledItem().WinningOverrides().OrderBy(static i => i.FormKey.ModKey, comparer).ThenBy(static i => i.FormKey.ID))
            {
                if (RecordPatcher.TryPatch(state, leveledItem, out LeveledItem? copy))
                    state.PatchMod.LeveledItems.Set(copy);
            }

            foreach (var leveledNpc in enabledAndExisting.LeveledNpc().WinningOverrides().OrderBy(static i => i.FormKey.ModKey, comparer).ThenBy(static i => i.FormKey.ID))
            {
                if (RecordPatcher.TryPatch(state, leveledNpc, out LeveledNpc? copy))
                    state.PatchMod.LeveledNpcs.Set(copy);
            }

            foreach (var leveledSpell in enabledAndExisting.LeveledSpell().WinningOverrides().OrderBy(static i => i.FormKey.ModKey, comparer).ThenBy(static i => i.FormKey.ID))
            {
                if (RecordPatcher.TryPatch(state, leveledSpell, out LeveledSpell? copy))
                    state.PatchMod.LeveledSpells.Set(copy);
            }
        }      
    }
}