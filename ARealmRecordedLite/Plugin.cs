global using static ARealmRecordedLite.Plugin;
global using static ARealmRecordedLite.Utilities.Tools;
using System;
using System.Reflection;
using ARealmRecordedLite.Managers;
using Dalamud.Plugin;

namespace ARealmRecordedLite;

public sealed class Plugin : IDalamudPlugin
{
    public static string PluginName => "A Realm Recorded Lite";
    public static Version? Version { get; private set; }

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        Version ??= Assembly.GetExecutingAssembly().GetName().Version;

        Service.Init(pluginInterface);
    }

    public void Dispose() => Service.Uninit();
}
