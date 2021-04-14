using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace leveledlistresolver
{
    internal static class Utility
    {
        internal static readonly ModKey SynthesisKey = ModKey.FromNameAndExtension("Synthesis.esp");

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

        internal static IEnumerable<T> IntersectWith<T>(this IEnumerable<T> lhs, IEnumerable<T> rhs)
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

        internal static bool IsNullOrEmptySublist(this ILeveledItemEntryGetter entry, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            if (entry.Data == null || entry.Data.Reference.IsNull)
                return true;

            if (entry.Data.Reference.TryResolve(linkCache, out var itemGetter) && itemGetter is ILeveledItemGetter leveledItem)
                return leveledItem.Entries is null || leveledItem.Entries.Count is 0;

            return false;
        }

        internal static bool IsNullOrEmptySublist(this ILeveledNpcEntryGetter entry, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            if (entry.Data == null || entry.Data.Reference.IsNull)
                return true;

            if (entry.Data.Reference.TryResolve(linkCache, out var npcSpawnGetter) && npcSpawnGetter is ILeveledNpcGetter leveledNpc)
                return leveledNpc.Entries is null || leveledNpc.Entries.Count is 0;

            return false;
        }
    }
}
