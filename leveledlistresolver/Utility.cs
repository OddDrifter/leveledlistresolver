using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace leveledlistresolver
{
    internal static class Utility
    {
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
    }
}
