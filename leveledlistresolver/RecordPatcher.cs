using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using System;
using System.Diagnostics.CodeAnalysis;

namespace leveledlistresolver
{
    interface IRecordPatcher<TGet, TSet>
        where TGet : class, IMajorRecordGetter
        where TSet : class, IMajorRecord, TGet
    {
        bool Try(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, FormKey formKey, [MaybeNullWhen(false)] out TSet? setter);
    }

    sealed partial class RecordPatcher
    {
        private static RecordPatcher Instance { get; } = new();

        public static bool TryPatch<TGet, TSet>(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, TGet getter, [NotNullWhen(true)] out TSet? setter)
            where TGet : class, IMajorRecordGetter
            where TSet : class, IMajorRecord, TGet
        {
            var instance = Instance as IRecordPatcher<TGet, TSet> ?? throw new NotImplementedException();
            return instance.Try(state, getter.FormKey, out setter);
        }
    }
}
