using System;
using System.Runtime.InteropServices;
using ARealmRecordedLite.Managers;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace ARealmRecordedLite.Utilities;

public static unsafe class Tools
{
    public static void SetConditionFlag(ConditionFlag flag, bool b) => *(bool*)(Service.Condition.Address + (int)flag) = b;

    public static string ReadCString(nint address) => Marshal.PtrToStringUTF8(address);
    
    public static void WriteCString(nint address, string str)
    {
        try
        {
            for (var i = 0; i < str.Length; i++)
            {
                var c = str[i];
                Marshal.WriteByte(address + i, Convert.ToByte(c));
            }
        }
        catch
        {
            // ignored
        }

        Marshal.WriteByte(address + str.Length, 0);
    }
    
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
