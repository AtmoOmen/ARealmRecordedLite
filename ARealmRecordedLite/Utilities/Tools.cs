using ARealmRecordedLite.Managers;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace ARealmRecordedLite.Utilities;

public static unsafe class Tools
{
    public static AtkUnitBase* GetAddonByName(string name) => GetAddonByName<AtkUnitBase>(name);
    
    public static T* GetAddonByName<T>(string addonName) where T : unmanaged
    {
        var a = Service.Gui.GetAddonByName(addonName);
        if (a == nint.Zero) return null;

        return (T*)a;
    }

    public static bool IsAddonAndNodesReady(AtkUnitBase* UI) =>
        UI                      != null && UI->IsVisible && UI->UldManager.LoadedState == AtkLoadState.Loaded && UI->RootNode != null &&
        UI->RootNode->ChildNode != null && UI->UldManager.NodeList                     != null;
}
