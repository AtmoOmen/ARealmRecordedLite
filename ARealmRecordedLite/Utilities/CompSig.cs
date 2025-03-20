using System;
using System.Runtime.InteropServices;
using ARealmRecordedLite.Managers;
using Dalamud.Game;
using Dalamud.Hooking;

namespace ARealmRecordedLite.Utilities;

/// <summary>
/// Composite Signatures 复合签名
/// </summary>
public record CompSig(string Signature, string? SignatureCN = null)
{
    public static bool IsClientCN => Service.ClientState.ClientLanguage == (ClientLanguage)4;

    public string? Get() => TryGet(out var sig) ? sig : null;

    public bool TryGet(out string? signature)
    {
        signature = IsClientCN && !string.IsNullOrWhiteSpace(SignatureCN) ? SignatureCN : Signature;
        return !string.IsNullOrWhiteSpace(signature);
    }

    private bool TryGetValidSignature(out string sig)
        => TryGet(out sig!) && !string.IsNullOrWhiteSpace(sig);

    public nint ScanText()
        => TryGetValidSignature(out var sig) ? Service.SigScanner.ScanText(sig) : nint.Zero;

    public unsafe T* ScanText<T>() where T : unmanaged
        => TryGetValidSignature(out var sig) ? (T*)Service.SigScanner.ScanText(sig) : null;

    public nint GetStatic(int offset = 0)
        => TryGetValidSignature(out var sig) ? Service.SigScanner.GetStaticAddressFromSig(sig, offset) : nint.Zero;

    public unsafe T* GetStatic<T>(int offset = 0) where T : unmanaged
        => TryGetValidSignature(out var sig) ? (T*)Service.SigScanner.GetStaticAddressFromSig(sig, offset) : null;

    public T GetDelegate<T>() where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(ScanText());

    public Hook<T> GetHook<T>(T detour) where T : Delegate
        => Service.Hook.HookFromSignature(Get() ?? string.Empty, detour);
}
