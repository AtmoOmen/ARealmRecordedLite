using Dalamud.Game;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Interface;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MemoryPatch = ARealmRecordedLite.Utilities.MemoryPatch;

namespace ARealmRecordedLite.Managers;

public class Service
{
    public static void Init(IDalamudPluginInterface pluginInterface)
    {
        PI        = pluginInterface;
        UiBuilder = pluginInterface.UiBuilder;
        
        pluginInterface.Create<Service>();

        Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Config.Init();

        CoreManager.Init();
        WindowManager.Init();
    }

    public static void Uninit()
    {
        WindowManager.Uninit();
        CoreManager.Uninit();
        
        Config.Uninit();

        MemoryPatch.DisposeAll();
    }

    public static Configuration  Config         { get; private set; } = null!;
    public static WindowManager  WindowManager  { get; private set; } = new();
    
    [PluginService] public static IAddonLifecycle      AddonLifecycle { get; private set; } = null!;
    [PluginService] public static IClientState         ClientState    { get; private set; } = null!;
    [PluginService] public static ICondition           Condition      { get; private set; } = null!;
    [PluginService] public static IDataManager         Data           { get; private set; } = null!;
    [PluginService] public static IFramework           Framework      { get; private set; } = null!;
    [PluginService] public static IGameConfig          GameConfig     { get; private set; } = null!;
    [PluginService] public static IGameGui             Gui            { get; private set; } = null!;
    [PluginService] public static IGameInteropProvider Hook           { get; private set; } = null!;
    [PluginService] public static IPluginLog           Log            { get; private set; } = null!;
    [PluginService] public static ITargetManager       Targets        { get; private set; } = null!;
    [PluginService] public static ITextureProvider     Texture        { get; private set; } = null!;

    public static IDalamudPluginInterface PI         { get; private set; } = null!;
    public static IUiBuilder              UiBuilder  { get; private set; } = null!;
    public static SigScanner              SigScanner { get; private set; } = new();
}
