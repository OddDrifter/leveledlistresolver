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
        public FormKey FormKey { get => Base?.FormKey ?? FormKey.Null; }
        public ImmutableDictionary<ModKey, HashSet<ModKey>> Adjacents { get; }
        public ImmutableHashSet<TMajorGetter> ExtentRecords { get; }

        public MajorRecordGraphBase(IPatcherState<TMod, TModGetter> state, in FormKey formKey)
        {
            patchMod = state.PatchMod;
            gameRelease = state.GameRelease;
            linkCache = state.LinkCache;

            var contexts = linkCache.ResolveAllContexts<TMajor, TMajorGetter>(formKey).Reverse()
                .ToDictionary(ctx => ctx.ModKey, ctx => ctx.Record);
            
            Base = contexts.First().Value;
            
            var comparer = ModKey.LoadOrderComparer(contexts.Keys.ToList());
            var modKeys = ImmutableSortedSet.CreateRange(comparer, contexts.Keys);
            var mods = modKeys.SelectWhere<ModKey, TModGetter?>(state.LoadOrder.TryGetIfEnabledAndExists).NotNull();

            Adjacents = mods.Aggregate(ImmutableDictionary.CreateBuilder<ModKey, HashSet<ModKey>>(), (builder, mod) =>
            {
                builder.Add(mod.ModKey, new());

                var masters = mod.MasterReferences
                    .Select(reference => reference.Master)
                    .Intersect(modKeys);               

                foreach (var master in masters)
                    if (builder[master].Overlaps(masters) is false)
                        builder[master].Add(mod.ModKey);

                return builder;
            }).ToImmutable();

            ExtentRecords = Adjacents.Where(kvp => kvp.Value.Count is 0 || kvp.Value.Contains(patchMod.ModKey))
                .Select(kvp => contexts[kvp.Key])
                .ToImmutableHashSet();

            Console.WriteLine(Environment.NewLine + this);
        }

        public string GetEditorId()
        {
            return ExtentRecords.Where(record => record.EditorID is not null && record.EditorID.Equals(Base.EditorID, StringComparison.InvariantCulture) is false)
                .DefaultIfEmpty(Base).Last().EditorID ?? Guid.NewGuid().ToString();
        }

        public override string ToString()
        {
            return ToString(FormKey.ModKey);
        }

        public string ToString(ModKey startPoint) 
        {
            if (Adjacents.ContainsKey(startPoint) is false)
                return string.Empty;

            StringBuilder builder = new($"{GetEditorId()} [{FormKey}]");

            var extents = Adjacents.Where(kvp => kvp.Value.Count is 0);
            foreach (var (extent, _) in extents)
                Visit(startPoint, extent, ImmutableList.Create(startPoint));

            void Visit(ModKey start, ModKey end, in ImmutableList<ModKey> visited)
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
