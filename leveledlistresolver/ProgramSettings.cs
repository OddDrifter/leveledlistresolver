using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.WPF.Reflection.Attributes;
using System.Collections.Generic;

namespace leveledlistresolver
{
    internal class ProgramSettings
    {
        [SettingName("Plugin Blacklist")]
        public HashSet<ModKey> BlacklistedPlugins { get; set; } = new();

        [SettingName("Remove empty sublists")]
        [Tooltip("Remove empty sublists from leveled lists. Some mods may add these intentionally.")]
        public bool RemoveEmptySublists { get; set; } = false;
    }
}
