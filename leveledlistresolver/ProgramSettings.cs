using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.WPF.Reflection.Attributes;

namespace leveledlistresolver
{
    internal class ProgramSettings
    {
        [SettingName("Remove empty sublists")]
        [Tooltip("Remove empty sublists from leveled lists. Some mods may add these intentionally.")]
        public bool RemoveEmptySublists { get; set; } = false;
    }
}
