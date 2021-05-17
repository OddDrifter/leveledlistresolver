using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace leveledlistresolver
{
    public abstract class MajorRecordGraphBase<TMod, TModGetter, TMajor, TMajorGetter>
        where TMod : class, IMod, TModGetter
        where TModGetter : class, IModGetter
        where TMajor : class, IMajorRecordCommon, TMajorGetter
        where TMajorGetter : class, IMajorRecordCommonGetter
    {
        protected readonly TMod patchMod;
        protected readonly GameRelease gameRelease;
        protected readonly ILinkCache<TMod, TModGetter> linkCache;

        public TMajorGetter Base { get; }
        public TMajorGetter ExtentBase { get; }
        public FormKey FormKey { get => Base?.FormKey ?? FormKey.Null; }
        public ImmutableDictionary<ModKey, HashSet<ModKey>> Adjacents { get; }
        public ImmutableHashSet<TMajorGetter> ExtentRecords { get; }

        public MajorRecordGraphBase(IPatcherState<TMod, TModGetter> state, in FormKey formKey)
        {
            patchMod = state.PatchMod;
            gameRelease = state.GameRelease;
            linkCache = state.LinkCache;

            var contexts = linkCache.ResolveAllContexts<TMajor, TMajorGetter>(formKey).Reverse().ToImmutableList();

            Console.Write($"{Environment.NewLine}{contexts.ItemRef(0).Record.EditorID} [{formKey}]");

            var comparer = ModKey.LoadOrderComparer(contexts.ConvertAll(static ctx => ctx.ModKey));
            var contextDictionary = contexts.ToImmutableSortedDictionary(ctx => ctx.ModKey, ctx => ctx.Record, comparer);
            
            Base = contextDictionary[formKey.ModKey];

            var modKeys = contextDictionary.Keys;
            var mods = modKeys.SelectWhere<ModKey, TModGetter?>(state.LoadOrder.TryGetIfEnabledAndExists).NotNull()
                .ToDictionary(mod => mod.ModKey, mod => mod.MasterReferences.Select(refr => refr.Master).Intersect(modKeys).ToHashSet());

            var adjancentBuilder = ImmutableDictionary.CreateBuilder<ModKey, HashSet<ModKey>>();

            foreach (var (key, masters) in mods)
            {
                masters.UnionWith(masters.SelectMany(master => mods[master]).ToHashSet());

                foreach (var master in masters)
                {
                    if (adjancentBuilder[master].Overlaps(masters) is false)
                        adjancentBuilder[master].Add(key);
                }

                adjancentBuilder.Add(key, new());
            }

            Adjacents = adjancentBuilder.ToImmutable();

            var extents = Adjacents.Where(kvp => kvp.Value is { Count: 0 } || kvp.Value.Contains(patchMod.ModKey));
            ExtentRecords = extents.Select(kvp => contextDictionary[kvp.Key]).ToImmutableHashSet();
            
            Console.WriteLine(ToString(formKey.ModKey));

            var extentMaster = extents.Aggregate(modKeys, (keys, kvp) => keys.Intersect(mods[kvp.Key]))
                .OrderBy(key => key, comparer).DefaultIfEmpty(formKey.ModKey).Last();
            ExtentBase = contextDictionary[extentMaster];
        }

        public ushort GetFormVersion()
        {
            return gameRelease.GetDefaultFormVersion() ?? Base.FormVersion ?? throw RecordException.Enrich(new NullReferenceException("FormVersion was Null"), Base);
        }

        public string GetEditorID()
        {
            return ExtentRecords.LastOrDefault(record => 
                !record.EditorID?.Equals(Base.EditorID, StringComparison.InvariantCulture) ?? false
            )?.EditorID ?? Base.EditorID ?? Guid.NewGuid().ToString();
        }

        public override string ToString()
        {
            return $"{GetEditorID()} [{FormKey}]" + ToString(FormKey.ModKey);
        }

        public string ToString(ModKey startPoint) 
        {
            if (Adjacents.ContainsKey(startPoint) is false)
                return string.Empty;

            StringBuilder builder = new();

            var extents = Adjacents.Where(kvp => kvp.Value.Count is 0);

            foreach (var (extent, _) in extents)
                Visit(startPoint, extent, ImmutableList.Create(startPoint));

            void Visit(ModKey start, ModKey end, ImmutableList<ModKey> visited)
            {
                if (start == end)
                {
                    builder.Append(Environment.NewLine + string.Join(" -> ", visited));
                    return;
                }
                else
                {
                    foreach (var node in Adjacents[start])
                    {
                        Visit(node, end, visited.Add(node));
                    }
                }
            }

            return builder.ToString();
        }

        public abstract MajorRecord ToMajorRecord();
    }
}
