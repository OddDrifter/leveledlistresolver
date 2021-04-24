using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace leveledlistresolver
{
    internal static class Utility
    {
        internal static bool UnsortedEqual<T>(IEnumerable<T> lhs, IEnumerable<T> rhs) where T : class
        {
            _ = lhs ?? throw new ArgumentNullException(nameof(lhs));
            _ = rhs ?? throw new ArgumentNullException(nameof(rhs));

            var dictionary = lhs.GroupBy(value => value).ToDictionary(value => value.Key, value => value.Count());

            foreach (var item in rhs)
            {
                if (dictionary.TryGetValue(item, out var count) is false || count is 0)
                    return false;
                dictionary[item]--;
            }

            return dictionary.All(kvp => kvp.Value is 0);
        }

        internal static IEnumerable<T> ExceptWith<T>(this IEnumerable<T> lhs, IEnumerable<T> rhs)
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

        internal static bool IsNullOrEmptySublist(this ILeveledItemEntryGetter entry, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            if (entry.Data == null || entry.Data.Reference.IsNull)
                return true;

            if (entry.Data.Reference.TryResolve<ILeveledItemGetter>(linkCache, out var leveledItem))
                return leveledItem.Entries is null or { Count: 0 };

            return false;
        }

        internal static bool IsNullOrEmptySublist(this ILeveledNpcEntryGetter entry, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            if (entry.Data == null || entry.Data.Reference.IsNull)
                return true;

            if (entry.Data.Reference.TryResolve<ILeveledNpcGetter>(linkCache, out var leveledNpc))
                return leveledNpc.Entries is null or { Count: 0 };

            return false;
        }

        public static int CountExtents<TMajor, TMajorGetter>(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, FormKey formKey) 
            where TMajor : class, IMajorRecordCommon, TMajorGetter where TMajorGetter : class, IMajorRecordCommonGetter
        {
            var loadOrder = state.LoadOrder.PriorityOrder.OnlyEnabledAndExisting().Resolve()
                .ToImmutableDictionary(mod => mod.ModKey);

            var modKeys = state.LinkCache.ResolveAllContexts<TMajor, TMajorGetter>(formKey)
                .Select(context => context.ModKey)
                .ToImmutableHashSet()
                .Remove(state.PatchMod.ModKey);

            if (modKeys.Count is 1)
                return 1;

            var masters = modKeys.Select(key => loadOrder[key])
                .SelectMany(mod => mod.MasterReferences.Select(reference => reference.Master))
                .ToImmutableHashSet();

            return modKeys.Count(modKey => masters.Contains(modKey) is false);
        }
    }
}
