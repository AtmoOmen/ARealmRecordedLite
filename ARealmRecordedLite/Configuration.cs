using System;
using ARealmRecordedLite.Managers;
using Dalamud.Configuration;

namespace ARealmRecordedLite;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public string LastLoadedReplay = string.Empty;
    public bool   EnableRecordingIcon;
    public int    MaxAutoRenamedReplays = 30;
    public int    MaxDeletedReplays     = 10;
    public bool   EnableHideOwnName;
    public bool   EnableQuickLoad = true;
    public bool   EnableJumpToTime;
    public float  MaxSeekDelta      = 100;
    public float  CustomSpeedPreset = 30;
    public bool   EnableWaymarks    = true;

    public void Init() { }

    public void Save() => Service.PI.SavePluginConfig(this);

    public void Uninit() { }
}
