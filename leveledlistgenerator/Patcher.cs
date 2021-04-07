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
                var copy = baseRecord.DeepCopy();

                copy.FormVersion = 44;
                copy.EditorID = FindEditorID(baseRecord, overrides);
                copy.ChanceNone = FindChanceNone(baseRecord.ChanceNone, overrides, record => record.ChanceNone);
                copy.Entries = FindEntries(baseRecord, overrides, record => record.Entries).Select(r => r.DeepCopy()).ToExtendedList();
                copy.Global = overrides.Where(record => record.Global != baseRecord.Global).DefaultIfEmpty(baseRecord).Last().Global.AsNullable();

                foreach (var leveledItem in overrides.Where(record => record.Flags != baseRecord.Flags))
                {
                    foreach (var flag in Enum.GetValues<LeveledItem.Flag>())
                    {
                        if ((leveledItem.Flags & flag) == flag) 
                            copy.Flags |= flag;

                        if ((~leveledItem.Flags & baseRecord.Flags & flag) == flag) 
                            copy.Flags &= ~flag;
                    }
                }

                var itemsRemoved = copy.Entries.RemoveAll(entry => IsNullOrEmptySublist(entry, state.LinkCache));

                if (itemsRemoved == 0 && copy.Equals(overrides.Last(), leveledItemsMask) && 
                    copy.Entries.IntersectWith(overrides.Last().Entries.EmptyIfNull()).CompareCount(copy.Entries) == 0)
                {
                    Console.WriteLine($"Skipping [{copy.FormKey}] {copy.EditorID}");
                    continue;
                }

                state.PatchMod.LeveledItems.Set(copy);
            }

            var leveledNpcs = loadOrder.PriorityOrder.OnlyEnabled().WinningOverrides<ILeveledNpcGetter>();
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
            }

            //var leveledSpells = loadOrder.PriorityOrder.OnlyEnabled().WinningOverrides<ILeveledSpellGetter>()
            //    .TryFindOverrides<ILeveledSpellGetter, ILeveledSpellEntryGetter>(state.LinkCache);

            Console.WriteLine("\nReport any issues at https://github.com/OddDrifter/leveledlistgenerator/issues \n");
        }

        static IEnumerable<T> ExceptWith<T>(this IEnumerable<T> lhs, IEnumerable<T> rhs)
        {
            _ = lhs ?? throw new ArgumentNullException(nameof(lhs));
            _ = rhs ?? throw new ArgumentNullException(nameof(rhs));

            if (!lhs.Any())
                yield break;

            var items = rhs.ToList();
            foreach (var item in lhs)
            {
                if (items.Remove(item))
                    continue;
                yield return item;
            }
        }

        static IEnumerable<T> IntersectWith<T>(this IEnumerable<T> lhs, IEnumerable<T> rhs) 
        {
            _ = lhs ?? throw new ArgumentNullException(nameof(lhs));
            _ = rhs ?? throw new ArgumentNullException(nameof(rhs));

            if (!lhs.Any() || !rhs.Any())
                yield break;

            var items = rhs.ToList();
            foreach (var item in lhs)
            {
                if (items.Remove(item))
                {
                    yield return item;
                    if (!items.Any()) 
                        yield break;
                }
            }
        }

        private static string FindEditorID<T>(T baseRecord, IEnumerable<T> overrides) where T : class, IMajorRecordGetter
        {
            if (overrides.LastOrDefault(value => !string.IsNullOrEmpty(value.EditorID) && value.EditorID.Equals(baseRecord.EditorID, StringComparison.InvariantCulture) is false) is string editorId)
            {
                return editorId;
            }
            else if (string.IsNullOrEmpty(baseRecord.EditorID) is false)
            {
                return baseRecord.EditorID!;
            }
            return Guid.NewGuid().ToString();
        }

        private static T FindChanceNone<T, U>(T baseValue, IEnumerable<U> sequence, Func<U, T> func) where T : unmanaged
        {
            return sequence.Select(func).Where(value => baseValue.Equals(value) is false).DefaultIfEmpty(baseValue).Last();
        }

        private static IEnumerable<TMinor> FindEntries<TMajor, TMinor>(TMajor baseRecord, IEnumerable<TMajor> overrides, Func<TMajor, IEnumerable<TMinor>?> entrySelector) where TMajor : class, IMajorRecordGetter
        {
            int itemCount = 0;
            List<TMinor> itemsAdded = new();
            List<TMinor> itemsRemoved = new();
            List<TMinor> itemsReplaced = new();
            ImmutableList<TMinor> itemsIntersected = ImmutableList.CreateRange(entrySelector(baseRecord) ?? Array.Empty<TMinor>());
            
            foreach (var window in overrides.Select(entrySelector).Select(EnumerableExt.EmptyIfNull).Window(2))
            {
                var left = window[0];
                var right = window[^1];

                var intersection = left.IntersectWith(right);
                var added = right.ExceptWith(intersection);
                var removed = left.ExceptWith(intersection);

                if (added.CompareCount(removed) == 0)
                    itemsReplaced.AddRange(removed);

                itemsAdded.AddRange(added);
                itemsRemoved.AddRange(removed.IntersectWith(itemsIntersected));

                itemsIntersected = ImmutableList.CreateRange(itemsIntersected.IntersectWith(intersection));
            }

            foreach (var item in itemsIntersected)
            {
                if (itemCount++ < 255) yield return item;
            }

            foreach (var item in itemsAdded)
            {
                if (itemsReplaced.Remove(item))
                    continue;

                if (itemsRemoved.Remove(item) is false)
                {
                    if (itemCount++ < 255) yield return item;
                }
            }
        }


        private static IEnumerable<(ILeveledItemGetter, IEnumerable<ILeveledItemGetter>)> FindRecordsToOverride(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, params ILeveledItemGetter[] getters)
        {
            foreach (var getter in getters)
            {
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