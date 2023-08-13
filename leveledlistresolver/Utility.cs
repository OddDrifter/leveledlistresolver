using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace leveledlistresolver
{
    internal static class Utility
    {
        public static uint Timestamp { get; } = (uint)(Math.Max(1, DateTime.Today.Year - 2000) << 9 | DateTime.Today.Month << 5 | DateTime.Today.Day);
        
        internal static IEnumerable<T> IntersectExt<T>(this IEnumerable<T> source, IEnumerable<T>? other, IEqualityComparer<T>? comparer = null) where T : class
        {
            var _comparer = comparer ?? EqualityComparer<T>.Default;

            if (!source.Any() || other == null || !other.Any())
                yield break;

            var set = new HashSet<T>(source, _comparer);
            set.IntersectWith(other);
            var dict = new Dictionary<T, int>(_comparer);

            foreach (var it in other)
            {
                if (!set.Contains(it))
                    continue;

                if (!dict.TryAdd(it, 1))
                    dict[it]++;
            }

            foreach (var it in source)
            {
                if (dict.TryGetValue(it, out int count) && count > 0)
                {
                    count--;
                    dict[it] = count;
                    if (count <= 0)
                        dict.Remove(it);
                    yield return it;
                }
            }
        }

        internal static bool UnsortedEqual<T>(this IReadOnlyList<T>? first, IReadOnlyList<T>? second) where T : class
        {
            if (first is null)
                return second is null;

            if (second is null)
                return false;

            if (first.Count != second.Count)
                return false;

            var dictionary = new Dictionary<T, int>();

            foreach (var it in first)
            {
                if (!dictionary.TryAdd(it, 1))
                    dictionary[it]++;
            }

            foreach (var it in second)
            {
                if (!dictionary.TryGetValue(it, out var count) || count is 0)
                    return false;
                dictionary[it]--;
            }

            return dictionary.Values.All(static i => i is 0);
        }

        public static IEnumerable<T> DisjunctLeft<T>(this IEnumerable<T> left, IEnumerable<T>? right, IEqualityComparer<T>? comparer = null) where T : notnull
        {
            if (right == null || !right.Any())
            {
                foreach (var it in left)
                    yield return it;
                yield break;
            }

            var dict = new Dictionary<T, int>(comparer ?? EqualityComparer<T>.Default);

            foreach (var it in right)
            {
                if (!dict.TryAdd(it, 1))
                    dict[it]++;
            }

            foreach (var it in left)
            {
                if (dict.TryGetValue(it, out int count) && count > 0)
                {
                    dict[it]--;
                    continue;
                }

                yield return it;
            }
        }    

        internal static bool IsNullEntry(this ILeveledItemEntryGetter entry)
        {
            return entry is { Data: null or { Reference.IsNull: true } };
        }

        internal static bool IsNullEntry(this ILeveledNpcEntryGetter entry)
        {
            return entry is { Data: null or { Reference.IsNull: true } };
        }

        internal static bool IsNullEntry(this ILeveledSpellEntryGetter entry)
        {
            return entry is { Data: null or { Reference.IsNull: true } };
        }

        internal static bool IsNullOrEmptySublist(this ILeveledItemEntryGetter entry, ILinkCache linkCache)
        {
            if (entry is { Data: null or { Reference.IsNull: true } })
                return true;
            return entry.Data.Reference.TryResolve<ILeveledItemGetter>(linkCache) is { Entries: null or { Count: 0 } };
        }

        internal static bool IsNullOrEmptySublist(this ILeveledNpcEntryGetter entry, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            if (entry is { Data: null or { Reference.IsNull: true } })
                return true;
            return entry.Data.Reference.TryResolve<ILeveledNpcGetter>(linkCache) is { Entries: null or { Count: 0 } };
        }

        internal static bool IsNullOrEmptySublist(this ILeveledSpellEntryGetter entry, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            if (entry is { Data: null or { Reference.IsNull: true } })
                return true;
            return entry.Data.Reference.TryResolve<ILeveledSpellGetter>(linkCache) is { Entries: null or { Count: 0 } };
        }

        internal static TGet GetLowestOverride<TGet>(this ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, FormKey formKey) where TGet : class, IMajorRecordGetter
        {
            var contexts = linkCache.ResolveAllSimpleContexts<TGet>(formKey).ToArray();
            var origin = contexts[^1].Record;

            if (contexts.Length <= 2)
            {
                if (contexts.Length == 0)
                    throw new InvalidOperationException();
                return origin;
            }

            var keys = Array.ConvertAll(contexts, static i => i.ModKey).ToHashSet();
            foreach (var ctx in contexts)
            {
                var masters = linkCache.PriorityOrder.FirstOrDefault(i => i.ModKey == ctx.ModKey)?.MasterReferences.Select(static i => i.Master) ?? Enumerable.Empty<ModKey>();
                keys.IntersectWith(masters);
            }

            if (keys.Count > 0)
                return contexts.FirstOrDefault(i => keys.Contains(i.ModKey))?.Record ?? origin;

            return origin;
        }

        internal static IEnumerable<IModContext<TGet>> GetExtentContexts<TGet>(this ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, FormKey formKey)
            where TGet : class, IMajorRecordGetter
        {
            var arr = linkCache.ResolveAllSimpleContexts<TGet>(formKey).ToArray();

            if (arr.Length <= 2)
            {
                if (arr.Length > 0)
                    yield return arr[0];
                yield break;
            }

            var refs = new HashSet<ModKey>();
            var keys = Array.ConvertAll(arr, i => i.ModKey);

            foreach (var ctx in arr[..^1])
            {
                if (!refs.Contains(ctx.ModKey))
                {
                    var index = linkCache.ListedOrder.IndexOf(ctx.ModKey, static (i, k) => i.ModKey == k);
                    refs.UnionWith(linkCache.ListedOrder[index].MasterReferences.Select(static i => i.Master) ?? Enumerable.Empty<ModKey>());
                    refs.IntersectWith(keys);
                    yield return ctx;
                }
            }
        }

        internal static void Deconstruct<TGet>(this IModContext<TGet> modContext, out ModKey modKey, out TGet record)
            where TGet : class, IMajorRecordGetter
        {
            modKey = modContext.ModKey;
            record = modContext.Record;
        }

        internal static void Deconstruct<TMod, TModGetter, TSet, TGet>(this IModContext<TMod, TModGetter, TSet, TGet> modContext, out ModKey modKey, out TGet record)
            where TModGetter : class, IModGetter
            where TMod : class, IMod, TModGetter
            where TGet : class, IMajorRecordGetter
            where TSet : class, IMajorRecord, TGet
        {
            modKey = modContext.ModKey;
            record = modContext.Record;
        }
    }
}
