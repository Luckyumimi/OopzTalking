using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace OopzTalking;

public enum NonXivUsersDisplayMode {
    Off = 0,
    BelowPartyList = 1,
    ManuallyPositioned = 2,
}

public enum IndicatorStyle {
    Imgui = 0,
    Atk = 1,
}

[Serializable]
public sealed class Configuration: IPluginConfiguration {
    // the below exist just to make saving less cumbersome
    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    // oopz 不需要 AccessToken（本地 WebSocket 无认证），已移除

    public List<AssignmentEntry> IndividualAssignments { get; set; } = new();
    public NonXivUsersDisplayMode NonXivUsersDisplayMode { get; set; } = NonXivUsersDisplayMode.BelowPartyList;
    public bool ShowNonXivUsersAlways { get; set; } = false;
    public int NonXivUsersX { get; set; } = 10;
    public int NonXivUsersY { get; set; } = 10;
    public bool ShowIndicators { get; set; } = true;
    public bool ShowUnmatchedUsers { get; set; } = true;
    public IndicatorStyle IndicatorStyle { get; set; } = IndicatorStyle.Imgui;
    public bool UseRoundedCorners { get; set; } = true;

    /// <summary>oopz 本地 WebSocket 端口，默认 10274。</summary>
    public int Port { get; set; } = 10274;

    // colours are ABGR
    public uint ColourUnmatched { get; set; } = 0xFF00FFFF; // yellow
    public uint ColourSpeaking { get; set; } = 0xFF00FF00; // green
    public uint ColourMuted { get; set; } = 0xFF808000; // teal
    public uint ColourDeafened { get; set; } = 0xFF0000FF; // red
    public int Version { get; set; } = 0;

    public void Initialize(IDalamudPluginInterface pluginInterface) {
        this.pluginInterface = pluginInterface;
    }

    public void Save() {
        this.pluginInterface!.SavePluginConfig(this);
    }
}

public sealed class AssignmentEntry {
    public string CharacterName { get; set; } = string.Empty;

    // oopz 这边没有 DiscordID，改成直接用成员名做绑定
    public string OopzName { get; set; } = string.Empty;
}