using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;
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
        where TMajor : class, IMajorRecord, TMajorGetter
        where TMajorGetter : class, IMajorRecordGetter
    {
        protected readonly TMod patchMod;
        protected readonly GameRelease gameRelease;
        protected readonly ILinkCache<TMod, TModGetter> linkCache;

        public TMajorGetter Base { get; }
        public TMajorGetter ExtentBase { get; }
        public ModKey ModKey { get; }
        public FormKey FormKey { get => Base?.FormKey ?? FormKey.Null; }
        public ImmutableDictionary<ModKey, HashSet<ModKey>> Adjacents { get; }
        public ImmutableHashSet<TMajorGetter> ExtentRecords { get; }
        public bool IsInjected { get => ModKey != FormKey.ModKey; }

        public MajorRecordGraphBase(IPatcherState<TMod, TModGetter> state, in FormKey formKey)
        {
            patchMod = state.PatchMod;
            gameRelease = state.GameRelease;
            linkCache = state.LinkCache;

            var modContexts = linkCache.ResolveAllContexts<TMajor, TMajorGetter>(formKey).ToImmutableList().Reverse();
            var modKeys = modContexts.ConvertAll(static ctx => ctx.ModKey);

            (ModKey, Base) = modContexts[0];

            var contextDictionary = modContexts.ToImmutableDictionary(ctx => ctx.ModKey, ctx => ctx.Record);
            var mastersDictionary = modKeys.Select(modKey => state.LoadOrder.TryGetIfEnabledAndExists(modKey, out var modGetter) ? modGetter : null).NotNull()
                .ToDictionary(mod => mod.ModKey, mod => mod.MasterReferences.Select(refr => refr.Master).Intersect(modKeys).ToHashSet());

            var adjancentBuilder = modKeys.ToImmutableDictionary(key => key, key => new HashSet<ModKey>()).ToBuilder();

            foreach (var (modKey, masters) in mastersDictionary)
            {
                masters.UnionWith(masters.SelectMany(master => mastersDictionary[master]).ToHashSet());

                foreach (var master in masters)
                {
                    if (adjancentBuilder[master].Overlaps(masters) is false)
                    {
                        adjancentBuilder[master].Add(modKey);
                    }
                }
            }

            Adjacents = adjancentBuilder.ToImmutable();
            
            var comparer = ModKey.LoadOrderComparer(modKeys);

            var extentMods = Adjacents.Where(kvp => kvp.Value is { Count: 0 } || kvp.Value.Contains(patchMod.ModKey)).Select(kvp => kvp.Key);
            var extentMasters = extentMods.Aggregate(modKeys, (list, modKey) => list.FindAll(key => mastersDictionary[modKey].Contains(key))).Sort(comparer);

            ExtentBase = extentMasters.IsEmpty ? Base : contextDictionary[extentMasters[^1]];
            ExtentRecords = extentMods.Select(modKey => contextDictionary[modKey]).ToImmutableHashSet();
            
            Console.WriteLine(Environment.NewLine + this);
        }

        public ushort GetFormVersion()
        {
            return gameRelease.GetDefaultFormVersion() ?? Base.FormVersion ?? throw RecordException.Enrich(new NullReferenceException("FormVersion was Null"), Base);
        }

        public string GetEditorID()
        {
            if (ExtentRecords.LastOrDefault(record => (record.EditorID?.Equals(Base.EditorID, StringComparison.InvariantCulture) ?? false) is false)?.EditorID is { } editorID)
            {
                return editorID;
            }
            return Base.EditorID ?? Guid.NewGuid().ToString();
        }

        public override string ToString()
        {
            return ToString(ModKey);
        }

        public string ToString(in ModKey startPoint) 
        {
            if (Adjacents.ContainsKey(startPoint) is false)
            {
                return $"Starting key \"{startPoint}\" was not in dictionary for {FormKey}";
            }

            string header = IsInjected ? $"{GetEditorID()} [{FormKey} | Injected by {ModKey}]": $"{GetEditorID()} [{FormKey}]";
            StringBuilder builder = new(header);

            var start = ImmutableList.Create(startPoint);
            var extents = Adjacents.Where(kvp => kvp.Value.Count is 0);

            foreach (var (extent, _) in extents)
                Visit(startPoint, extent, start);

            void Visit(in ModKey start, in ModKey end, ImmutableList<ModKey> visited)
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
