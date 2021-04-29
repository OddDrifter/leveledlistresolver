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

            var toRemove = rhs.ToList();
            return lhs.Where(item => toRemove.Remove(item) is false);
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

        internal static int CountExtents<TMajor, TMajorGetter>(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, FormKey formKey) 
            where TMajor : class, IMajorRecordCommon, TMajorGetter where TMajorGetter : class, IMajorRecordCommonGetter
        {
            var modKeys = state.LinkCache.ResolveAllContexts<TMajor, TMajorGetter>(formKey)
                .Select(context => context.ModKey)
                .ToImmutableHashSet()
                .Remove(state.PatchMod.ModKey);

            var masters = modKeys.Aggregate(ImmutableHashSet.CreateBuilder<ModKey>(), (builder, modKey) =>
            {
                if (state.LoadOrder.TryGetIfEnabledAndExists(modKey, out var mod))
                    foreach (var master in mod.MasterReferences)
                        builder.Add(master.Master);
                return builder;
            }).ToImmutable();

            return modKeys.Count(modKey => masters.Contains(modKey) is false);
        }
    }
}
