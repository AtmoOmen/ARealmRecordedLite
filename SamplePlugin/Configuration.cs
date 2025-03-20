using System;
using Dalamud.Configuration;

namespace SamplePlugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;
    public string Name { get; set; } = string.Empty;

    public void Init()
    {
    }

    public void Save()
    {
        DService.PI.SavePluginConfig(this);
    }

    public void Uninit()
    {

    }
}
