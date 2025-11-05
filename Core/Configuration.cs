using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace AutomarketPro.Core
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 1;
        
        // Market settings
        public int UndercutAmount { get; set; } = 1;
        public int MinProfitThreshold { get; set; } = 100;
        public bool AutoUndercut { get; set; } = true;
        
        // Automation settings
        public int ActionDelay { get; set; } = 500;
        public int RetainerDelay { get; set; } = 2000;
        public bool ListOnlyMode { get; set; } = false;
        public bool VendorOnlyMode { get; set; } = false;
        
        // Filter settings
        public bool SkipHQItems { get; set; } = false;
        public bool SkipCollectables { get; set; } = true;
        public bool SkipGear { get; set; } = false;
        
        // Ignored items (item IDs that should be excluded from processing)
        public HashSet<uint> IgnoredItemIds { get; set; } = new HashSet<uint>();
        
        // Debug settings
        public bool DebugLogsEnabled { get; set; } = false;
        
        [NonSerialized]
        public IDalamudPluginInterface? pluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
        }
        
        public void Save()
        {
            pluginInterface?.SavePluginConfig(this);
        }
        
        public void ResetToDefaults()
        {
            UndercutAmount = 1;
            MinProfitThreshold = 100;
            AutoUndercut = true;
            ActionDelay = 500;
            RetainerDelay = 2000;
            ListOnlyMode = false;
            VendorOnlyMode = false;
            SkipHQItems = false;
            SkipCollectables = true;
            SkipGear = false;
            DebugLogsEnabled = false;
        }
    }
}

