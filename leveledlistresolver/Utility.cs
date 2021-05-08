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

        internal static IEnumerable<T> Without<T>(this IEnumerable<T> source, IEnumerable<T> other, IEqualityComparer<T>? comparer = null) where T : notnull
        {
            _ = source ?? throw new ArgumentNullException(nameof(source));
            _ = other ?? throw new ArgumentNullException(nameof(other));

            var _comparer = comparer ?? EqualityComparer<T>.Default;
            var _dictionary = other.GroupBy(value => value, _comparer).ToDictionary(value => value.Key, value => value.Count(), _comparer);

            foreach (var item in source)
            {
                if (_dictionary.TryGetValue(item, out var count))
                {
                    switch (count) 
                    {
                        case <= 0: 
                            yield return item;
                            break;
                        default: 
                            _dictionary[item]--;
                            break;
                    };
                    continue;
                }
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

        internal static bool IsNullOrEmptySublist(this ILeveledSpellEntryGetter entry, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            if (entry.Data == null || entry.Data.Reference.IsNull)
                return true;

            if (entry.Data.Reference.TryResolve<ILeveledSpellGetter>(linkCache, out var leveledSpell))
                return leveledSpell.Entries is null or { Count: 0 };

            return false;
        }

        internal static int CountExtents<TMajor, TMajorGetter>(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, in FormKey formKey) 
            where TMajor : class, IMajorRecordCommon, TMajorGetter where TMajorGetter : class, IMajorRecordCommonGetter
        {
            var modKeys = state.LinkCache.ResolveAllContexts<TMajor, TMajorGetter>(formKey)
                .Select(context => context.ModKey)
                .ToImmutableHashSet()
                .Remove(state.PatchMod.ModKey);

            if (modKeys.Count is <= 2)
                return 1;

            var masters = modKeys
                .SelectMany(modKey => state.LoadOrder.TryGetIfEnabledAndExists(modKey, out var mod) ? mod.MasterReferences : Array.Empty<IMasterReferenceGetter>())
                .Select(reference => reference.Master)
                .ToImmutableHashSet();

            return modKeys.Except(masters).Count;
        }
    }
}
