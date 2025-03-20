using System.Linq;
using ARealmRecordedLite.Windows;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;

namespace ARealmRecordedLite.Managers;

public class WindowManager
{
    public static WindowSystem? WindowSystem { get; private set; }

    internal void Init()
    {
        WindowSystem ??= new WindowSystem(PluginName);
        WindowSystem.RemoveAllWindows();
        
        InternalWindows.Init();
        
        Service.UiBuilder.Draw += DrawWindows;
        Service.UiBuilder.OpenMainUi += ToggleMainWindow;
    }

    private static void DrawWindows()
    {
        using var font = FontManager.UIFont.Push();
        WindowSystem?.Draw();
    }

    private static unsafe void ToggleMainWindow() => UIModule.Instance()->ExecuteMainCommand(76);

    public static bool AddWindow(Window? window)
    {
        if (WindowSystem == null || window == null) return false;

        var addedWindows = WindowSystem.Windows;
        if (addedWindows.Contains(window) || addedWindows.Any(x => x.WindowName == window.WindowName))
            return false;

        WindowSystem.AddWindow(window);
        return true;
    }

    public static bool RemoveWindow(Window? window)
    {
        if (WindowSystem == null || window == null) return false;

        var addedWindows = WindowSystem.Windows;
        if (!addedWindows.Contains(window)) return false;

        WindowSystem.RemoveWindow(window);
        return true;
    }

    public static T? Get<T>() where T : Window
        => WindowSystem?.Windows.FirstOrDefault(x => x.GetType() == typeof(T)) as T;

    internal void Uninit()
    {
        Service.UiBuilder.Draw -= DrawWindows;
        Service.UiBuilder.OpenMainUi -= ToggleMainWindow;
        
        InternalWindows.Uninit();
        
        WindowSystem?.RemoveAllWindows();
        WindowSystem = null;
    }

    private static class InternalWindows
    {
        internal static void Init()
        {
            AddWindow(new ReplayListWindow());
            AddWindow(new PlaybackControlWindow());
        }

        internal static void Uninit()
        {
            Get<ReplayListWindow>()?.Dispose();
            Get<PlaybackControlWindow>()?.Dispose();
        }
    }
}
