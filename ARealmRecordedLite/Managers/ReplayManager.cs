using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ARealmRecordedLite.Utilities;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Config;
using Dalamud.Game.Gui.Toast;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel.Sheets;
using MemoryPatch = ARealmRecordedLite.Utilities.MemoryPatch;

namespace ARealmRecordedLite.Managers;

public static unsafe class ReplayManager
{
    #region MemoryPatch

    private static MemoryPatch? CreateGetReplaySegmentHookPatch;

    private static readonly MemoryPatch RemoveRecordReadyToastPatch =
        new("BA CB 07 00 00 48 8B CF E8", [0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90]);

    private static readonly MemoryPatch AlwaysRecordPatch =
        new("24 ?? 3C ?? 75 ?? 48 8B 0D ?? ?? ?? ?? BA", [0xEB, 0x25]);

    private static readonly MemoryPatch SeIsABunchOfClownsPatch =
        new("F6 40 ?? 02 74 04 B0 01 EB 02 32 C0 40 84 FF", [0x90, 0x90, 0x90, 0x90, 0x90, 0x90]);

    private static readonly MemoryPatch InstantFadeOutPatch =
        new("44 8D 47 0A 33 D2", [null, null, 0x07, 0x90]);

    private static readonly MemoryPatch InstantFadeInPatch =
        new("44 8D 42 0A 41 FF 92 ?? ?? 00 00 48 8B 5C 24", [null, null, null, 0x01]);

    public static readonly MemoryPatch ReplaceLocalPlayerNamePatch =
        new("75 ?? 48 8D 4C 24 ?? E8 ?? ?? ?? ?? F6 05", [0x90, 0x90]);
    
    private static readonly MemoryPatch RemoveProcessingLimitPatch =
        new("41 FF C7 48 39 43", [0x90, 0x90, 0x90]);

    private static readonly MemoryPatch RemoveProcessingLimitPatch2 =
        new("0F 87 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? 33 F6", [0x90, 0x90, 0x90, 0x90, 0x90, 0x90]);

    private static readonly MemoryPatch ForceFastForwardPatch =
        new("0F 83 ?? ?? ?? ?? 41 0F B7 46 02 4D 8D 46 0C", [0x90, 0x90, 0x90, 0x90, 0x90, 0x90]);

    #endregion

    #region Hook & Delagte Definitions

    private static Hook<ContentsReplayModule.OnZoneInPacketDelegate>?            GetZoneInPacketHook;
    private static Hook<ContentsReplayModule.InitializeRecordingDelegate>?       InitializeRecordingHook;
    private static Hook<ContentsReplayModule.RequestPlaybackDelegate>?           RequestPlaybackHook;
    private static Hook<ContentsReplayModule.ReceiveActorControlPacketDelegate>? ReceiveActorControlPacketHook;
    private static Hook<ContentsReplayModule.ReplayPacketDelegate>?              ReplayPacketHook;

    private static readonly CompSig OnSetChapterSig = new("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 48 89 7C 24 ?? 41 56 48 83 EC 30 48 8B F1 0F B6 EA");
    private delegate        void    OnSetChapterDelegate(ContentsReplayModule* contentsReplayModule, byte chapter);
    private static          Hook<OnSetChapterDelegate>? OnSetChapterHook;
    
    private static readonly CompSig FormatAddonTextTimestampSig = new("E8 ?? ?? ?? ?? 48 8B D0 8D 4F 0E");
    public delegate nint FormatAddonTextTimestampDelegate(
        nint raptureTextModule, uint addonSheetRow, int a3, uint hours, uint minutes, uint seconds, uint a7);
    private static Hook<FormatAddonTextTimestampDelegate>? FormatAddonTextTimestampHook;

    private static readonly CompSig DisplayRecordingOnDTRBarSig = new("E8 ?? ?? ?? ?? 44 0F B6 C0 BA 4F 00 00 00");
    private delegate bool DisplayRecordingOnDTRBarDelegate(nint agent);
    private static Hook<DisplayRecordingOnDTRBarDelegate>? DisplayRecordingOnDTRBarHook;

    private static readonly CompSig ContentDirectorSynchronizeSig =
        new("40 53 55 57 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 0F B6 81 ?? ?? ?? ??");
    private delegate void ContentDirectorSynchronizeDelegate(nint contentDirector);
    private static Hook<ContentDirectorSynchronizeDelegate>? ContentDirectorSynchronizeHook;

    private static readonly CompSig EventBeginSig =
        new("40 53 55 57 41 56 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 48 8B 59 08");
    private delegate nint EventBeginDelegate(nint a1, nint a2);
    private static Hook<EventBeginDelegate>? EventBeginHook;

    private static readonly CompSig WaymarkToggleSig = new("48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? EB 3B");
    private static          byte*   waymarkToggle;

    private static readonly CompSig ContentDirectorOffsetSig = new("F6 81 ?? ?? ?? ?? ?? 75 ?? 85 FF 75");
    private static          short   contentDirectorOffset;

    private static readonly CompSig DisplaySelectedDutyRecordingSig = new("E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? 39 6E 34");
    private delegate void DisplaySelectedDutyRecordingDelegate(nint a1);
    private static DisplaySelectedDutyRecordingDelegate? displaySelectedDutyRecording;

    private static readonly CompSig DeleteCharacterAtIndexSig = new("E8 ?? ?? ?? ?? 33 D2 48 8D 47 50");
    private delegate void DeleteCharacterAtIndexDelegate(CharacterManager* manager, int index);
    private static DeleteCharacterAtIndexDelegate? deleteCharacterAtIndex;

    public static readonly CompSig                             GetReplayDataSegmentSig = new("40 53 48 83 EC ?? F6 81 ?? ?? ?? ?? ?? 48 8B D9 0F 85 ?? ?? ?? ?? F6 81");
    public delegate        FFXIVReplay.DataSegment*            GetReplayDataSegmentDelegate(ContentsReplayModule* contentsReplayModule);
    private static         Hook<GetReplayDataSegmentDelegate>? GetReplayDataSegmentHook;

    public static readonly CompSig PlaybackUpdateSig = new("48 8B C4 53 48 81 EC ?? ?? ?? ?? F6 81 ?? ?? ?? ?? ?? 48 8B D9 0F 84 ?? ?? ?? ?? 48 89 68");
    public delegate        void PlaybackUpdateDelegate(ContentsReplayModule* contentsReplayModule);
    private static         Hook<PlaybackUpdateDelegate>? PlaybackUpdateHook;
    
    public static readonly CompSig                       ExecuteCommandSig = new("E8 ?? ?? ?? ?? 48 8B 06 48 8B CE FF 50 ?? E9 ?? ?? ?? ?? 49 8B CC");
    private delegate       nint                          ExecuteCommandDelegate(int command, int param1, int param2, int param3, int param4);
    private static         Hook<ExecuteCommandDelegate>? ExecuteCommandHook;

    #endregion
    
    private static readonly HashSet<int> ExecuteCommandFlagsToIgnore = [201, 1981];
    
    private static FFXIVReplay* LoadedReplay;
    private static byte         QuickLoadChapter;
    private static byte         SeekingChapter;
    private static uint         SeekingOffset;
    
    public static bool IsWaymarkVisible
    {
        get => (*waymarkToggle & 2) != 0;
        set
        {
            if (value)
                *waymarkToggle |= 2;
            else
                *waymarkToggle = (byte)(*waymarkToggle & ~2);
        }
    }

    public static void Init()
    {
        Service.Toast.Toast += OnToast;
        
        var address = Service.SigScanner.ScanModule("48 39 43 ?? 0F 83 ?? ?? ?? ?? 48 8B 4B");

        GetReplayDataSegmentHook ??= GetReplayDataSegmentSig.GetHook<GetReplayDataSegmentDelegate>(GetReplayDataSegmentDetour);
        CreateGetReplaySegmentHookPatch ??= new(address,
        [
            0x48, 0x8B, 0xCB,
            0xE8, .. BitConverter.GetBytes((int)(GetReplayDataSegmentHook.Address - (address + 0x8))),
            0x4C, 0x8B, 0xF0,
            0xEB, 0x3A,
            0x90
        ]);
        
        RemoveProcessingLimitPatch.Enable();
        CreateGetReplaySegmentHookPatch.Enable();
        RemoveRecordReadyToastPatch.Enable();
        AlwaysRecordPatch.Enable();
        SeIsABunchOfClownsPatch.Enable();
        InstantFadeOutPatch.Enable();
        InstantFadeInPatch.Enable();
        ReplaceLocalPlayerNamePatch.Enable();

        waymarkToggle                = WaymarkToggleSig.GetStatic<byte>() + 0x48;
        contentDirectorOffset        = *ContentDirectorOffsetSig.ScanText<short>();
        displaySelectedDutyRecording = DisplaySelectedDutyRecordingSig.GetDelegate<DisplaySelectedDutyRecordingDelegate>();
        deleteCharacterAtIndex       = DeleteCharacterAtIndexSig.GetDelegate<DeleteCharacterAtIndexDelegate>();

        DisplayRecordingOnDTRBarHook ??= DisplayRecordingOnDTRBarSig.GetHook<DisplayRecordingOnDTRBarDelegate>(DisplayRecordingOnDTRBarDetour);
        DisplayRecordingOnDTRBarHook.Enable();

        ContentDirectorSynchronizeHook ??=
            ContentDirectorSynchronizeSig.GetHook<ContentDirectorSynchronizeDelegate>(ContentDirectorSynchronizeDetour);

        EventBeginHook ??= EventBeginSig.GetHook<EventBeginDelegate>(EventBeginDetour);
        EventBeginHook.Enable();

        GetZoneInPacketHook ??=
            ContentsReplayModule.OnZoneInPacketSig.GetHook<ContentsReplayModule.OnZoneInPacketDelegate>(OnZoneInPacketDetour);
        GetZoneInPacketHook.Enable();

        InitializeRecordingHook ??=
            ContentsReplayModule.InitializeRecordingSig.GetHook<ContentsReplayModule.InitializeRecordingDelegate>(InitializeRecordingDetour);
        InitializeRecordingHook.Enable();

        PlaybackUpdateHook ??= PlaybackUpdateSig.GetHook<PlaybackUpdateDelegate>(PlaybackUpdateDetour);
        PlaybackUpdateHook.Enable();

        RequestPlaybackHook ??= ContentsReplayModule.RequestPlaybackSig.GetHook<ContentsReplayModule.RequestPlaybackDelegate>(RequestPlaybackDetour);
        RequestPlaybackHook.Enable();

        ReceiveActorControlPacketHook ??=
            ContentsReplayModule.ReceiveActorControlPacketSig.GetHook<ContentsReplayModule.ReceiveActorControlPacketDelegate>(
                ReceiveActorControlPacketDetour);
        ReceiveActorControlPacketHook.Enable();

        OnSetChapterHook ??= OnSetChapterSig.GetHook<OnSetChapterDelegate>(OnSetChapterDetour);
        OnSetChapterHook.Enable();

        ReplayPacketHook ??= ContentsReplayModule.ReplayPacketSig.GetHook<ContentsReplayModule.ReplayPacketDelegate>(ReplayPacketDetour);
        ReplayPacketHook.Enable();

        FormatAddonTextTimestampHook ??= FormatAddonTextTimestampSig.GetHook<FormatAddonTextTimestampDelegate>(FormatAddonTextTimestampDetour);
        FormatAddonTextTimestampHook.Enable();

        ExecuteCommandHook ??= ExecuteCommandSig.GetHook<ExecuteCommandDelegate>(OnExecuteCommandDetour);
        ExecuteCommandHook.Enable();
    }

    public static void Uninit()
    {
        RemoveProcessingLimitPatch.Disable();
        RemoveProcessingLimitPatch2.Disable();
        ForceFastForwardPatch.Disable();
        RemoveRecordReadyToastPatch.Disable();
        AlwaysRecordPatch.Disable();
        SeIsABunchOfClownsPatch.Disable();
        InstantFadeOutPatch.Disable();
        InstantFadeInPatch.Disable();
        ReplaceLocalPlayerNamePatch.Disable();
        
        ExecuteCommandHook?.Dispose();
        ExecuteCommandHook = null;

        CreateGetReplaySegmentHookPatch?.Dispose();
        CreateGetReplaySegmentHookPatch = null;

        GetReplayDataSegmentHook?.Dispose();
        GetReplayDataSegmentHook = null;

        GetZoneInPacketHook?.Dispose();
        GetZoneInPacketHook = null;

        InitializeRecordingHook?.Dispose();
        InitializeRecordingHook = null;

        PlaybackUpdateHook?.Dispose();
        PlaybackUpdateHook = null;

        RequestPlaybackHook?.Dispose();
        RequestPlaybackHook = null;

        ReceiveActorControlPacketHook?.Dispose();
        ReceiveActorControlPacketHook = null;

        OnSetChapterHook?.Dispose();
        OnSetChapterHook = null;

        ReplayPacketHook?.Dispose();
        ReplayPacketHook = null;

        FormatAddonTextTimestampHook?.Dispose();
        FormatAddonTextTimestampHook = null;

        DisplayRecordingOnDTRBarHook?.Dispose();
        DisplayRecordingOnDTRBarHook = null;

        ContentDirectorSynchronizeHook?.Dispose();
        ContentDirectorSynchronizeHook = null;

        EventBeginHook?.Dispose();
        EventBeginHook = null;
        
        Service.Toast.Toast -= OnToast;

        if (LoadedReplay == null) return;

        if (ContentsReplayModule.Instance()->InPlayback)
        {
            ContentsReplayModule.Instance()->PlaybackControls |= 8;
            Service.Log.Error("插件已卸载, 若不继续重新加载插件或录像可能导致文件损毁");
        }

        Marshal.FreeHGlobal((nint)LoadedReplay);
        LoadedReplay = null;
    }
    
    private static void OnToast(ref SeString message, ref ToastOptions options, ref bool isHandled)
    {
        if (isHandled || (!ContentsReplayModule.Instance()->IsLoadingChapter && ContentsReplayModule.Instance()->Speed < 5)) return;
        isHandled = true;
    }

    
    #region Hook Detour

    private static void OnZoneInPacketDetour(ContentsReplayModule* contentsReplayModule, uint gameObjectID, nint packet)
    {
        GetZoneInPacketHook.Original(contentsReplayModule, gameObjectID, packet);

        if ((contentsReplayModule->Status & 1) == 0) return;

        InitializeRecordingDetour(contentsReplayModule);
    }

    private static void InitializeRecordingDetour(ContentsReplayModule* contentsReplayModule)
    {
        var zoneID = contentsReplayModule->InitZonePacketData.TerritoryType;
        if (zoneID == 0) return;
        
        var contentID = Service.Data.GetExcelSheet<TerritoryType>().GetRow(zoneID).ContentFinderCondition.RowId;
        if (contentID == 0) return;
        
        contentsReplayModule->InitZonePacketData.ContentFinderCondition = (ushort)contentID;

        contentsReplayModule->FixNextReplaySaveSlot(Service.Config.MaxAutoRenamedReplays);
        InitializeRecordingHook.Original(contentsReplayModule);
        contentsReplayModule->BeginRecording();

        var header = contentsReplayModule->ReplayHeader;
        header.LocalCID                    = 0;
        contentsReplayModule->ReplayHeader = header;

        if (contentDirectorOffset > 0)
            ContentDirectorSynchronizeHook?.Enable();

        ReplayPacketManager.FlushBuffer();
    }

    private static bool RequestPlaybackDetour(ContentsReplayModule* contentsReplayModule, byte slot)
    {
        var                customSlot = slot == 100;
        FFXIVReplay.Header prevHeader = new();

        if (customSlot)
        {
            slot                                        = 0;
            prevHeader                                  = contentsReplayModule->SavedReplayHeaders[0];
            contentsReplayModule->SavedReplayHeaders[0] = ReplayFileManager.LastSelectedHeader;
        }
        else
            ReplayFileManager.LastSelectedReplay = null;

        var ret = RequestPlaybackHook.Original(contentsReplayModule, slot);

        if (customSlot)
            contentsReplayModule->SavedReplayHeaders[0] = prevHeader;

        return ret;
    }

    private static void ReceiveActorControlPacketDetour(ContentsReplayModule* contentsReplayModule, uint gameObjectID, nint packet)
    {
        ReceiveActorControlPacketHook.Original(contentsReplayModule, gameObjectID, packet);
        if (*(ushort*)packet != 931 || !*(bool*)(packet + 4)) return;

        UnloadReplay();

        if (string.IsNullOrEmpty(ReplayFileManager.LastSelectedReplay))
            LoadReplay(contentsReplayModule->CurrentReplaySlot);
        else
            ReplayManager.LoadReplay(ReplayFileManager.LastSelectedReplay);
    }

    private static void PlaybackUpdateDetour(ContentsReplayModule* contentsReplayModule)
    {
        GetReplayDataSegmentHook?.Enable();
        PlaybackUpdateHook.Original(contentsReplayModule);
        GetReplayDataSegmentHook?.Disable();

        ReplayFileManager.UpdateAutoRename();

        if (contentsReplayModule->IsRecording &&
            contentsReplayModule->ReplayChapters[0]->Type == 1)
            contentsReplayModule->ReplayChapters[0]->Type = 5;

        if (!contentsReplayModule->InPlayback) return;

        SetConditionFlag(ConditionFlag.OccupiedInCutSceneEvent, false);

        PlaybackUpdate(contentsReplayModule);
    }

    public static FFXIVReplay.DataSegment* GetReplayDataSegmentDetour(ContentsReplayModule* contentsReplayModule) =>
        ReplayManager.GetReplayDataSegment(contentsReplayModule);

    private static void OnSetChapterDetour(ContentsReplayModule* contentsReplayModule, byte chapter)
    {
        OnSetChapterHook.Original(contentsReplayModule, chapter);
        ReplayManager.OnSetChapter(contentsReplayModule, chapter);
    }

    private static nint FormatAddonTextTimestampDetour(
        nint raptureTextModule, uint addonSheetRow, int a3, uint hours, uint minutes,
        uint seconds,           uint a7)
    {
        var ret = FormatAddonTextTimestampHook.Original(raptureTextModule, addonSheetRow, a3, hours, minutes, seconds, a7);

        try
        {
            if (a3 > 64 || addonSheetRow != 3079 || !Service.PI.UiBuilder.ShouldModifyUi) return ret;

            var currentChapterMS = ContentsReplayModule.Instance()->ReplayChapters[a3 - 1]->MS;
            var nextChapterMS    = a3 < 64 ? ContentsReplayModule.Instance()->ReplayChapters[a3]->MS : ContentsReplayModule.Instance()->ReplayHeader.TotalMS;
            if (nextChapterMS < currentChapterMS)
                nextChapterMS = ContentsReplayModule.Instance()->ReplayHeader.TotalMS;

            var timespan = new TimeSpan(0, 0, 0, 0, (int)(nextChapterMS - currentChapterMS));
            WriteCString(ret + ReadCString(ret).Length, $" ({(int)timespan.TotalMinutes:D2}:{timespan.Seconds:D2})");
        }
        catch (Exception e)
        {
            Service.Log.Error(e, string.Empty);
        }

        return ret;
    }

    private static bool DisplayRecordingOnDTRBarDetour(nint agent) =>
        Service.Config.EnableRecordingIcon            &&
        ContentsReplayModule.Instance()->IsRecording &&
        Service.PI.UiBuilder.ShouldModifyUi;

    private static void ContentDirectorSynchronizeDetour(nint contentDirector)
    {
        if ((*(byte*)(contentDirector + contentDirectorOffset) & 12) == 12)
        {
            ContentsReplayModule.Instance()->Status |= 64;
            ContentDirectorSynchronizeHook.Disable();
        }

        ContentDirectorSynchronizeHook.Original(contentDirector);
    }

    private static nint EventBeginDetour(nint a1, nint a2) =>
        !ContentsReplayModule.Instance()->InPlayback                                                       ||
        !Service.GameConfig.UiConfig.TryGetBool(nameof(UiConfigOption.CutsceneSkipIsContents), out var b) ||
        !b
            ? EventBeginHook.Original(a1, a2)
            : nint.Zero;

    private static bool ReplayPacketDetour(ContentsReplayModule* contentsReplayModule, FFXIVReplay.DataSegment* segment, byte* data) =>
        ReplayPacketManager.ReplayPacket(segment, data) || ReplayPacketHook.Original(contentsReplayModule, segment, data);
    
    private static nint OnExecuteCommandDetour(int command, int param1, int param2, int param3, int param4)
    {
        if (!ContentsReplayModule.Instance()->InPlayback ||
            ExecuteCommandFlagsToIgnore.Contains(command))
            return ExecuteCommandHook.Original(command, param1, param2, param3, param4);

        if (command == 314) SetConditionFlag(ConditionFlag.WatchingCutscene, param1 != 0);

        return nint.Zero;
    }


    #endregion
    
    #region Control
    
    public static void DisplaySelectedDutyRecording(nint agent) => displaySelectedDutyRecording(agent);

    public static void DeleteCharacterAtIndex(int i) => deleteCharacterAtIndex(CharacterManager.Instance(), i);
    
    public static void PlaybackUpdate(ContentsReplayModule* contentsReplayModule)
    {
        if (LoadedReplay == null) return;

        contentsReplayModule->DataLoadType = 0;
        contentsReplayModule->DataOffset   = 0;

        if (QuickLoadChapter < 2) return;

        var seekedTime = contentsReplayModule->ReplayChapters[SeekingChapter]->MS;
        if (seekedTime > (int)(contentsReplayModule->Seek * 1000)) return;

        DoQuickLoad();
    }

    public static FFXIVReplay.DataSegment* GetReplayDataSegment(ContentsReplayModule* contentsReplayModule)
    {
        if (LoadedReplay == null) return null;

        if (SeekingOffset > 0 && SeekingOffset <= contentsReplayModule->OverallDataOffset)
        {
            ForceFastForwardPatch.Disable();
            SeekingOffset = 0;
        }

        if (Service.Config.MaxSeekDelta <= 100 || contentsReplayModule->SeekDelta >= Service.Config.MaxSeekDelta)
            RemoveProcessingLimitPatch2.Disable();
        else
            RemoveProcessingLimitPatch2.Enable();

        return LoadedReplay->GetDataSegment((uint)contentsReplayModule->OverallDataOffset);
    }

    public static void OnSetChapter(ContentsReplayModule* contentsReplayModule, byte chapter)
    {
        if (!Service.Config.EnableQuickLoad                    ||
            chapter                                      <= 0 ||
            contentsReplayModule->ReplayChapters.Length        < 2  ||
            ContentsReplayModule.GetCurrentChapter() + 1 == chapter)
            return;

        QuickLoadChapter = chapter;
        SeekingChapter   = 0;
        DoQuickLoad();
    }

    public static bool LoadReplay(int slot) => LoadReplay(Path.Join(ReplayFileManager.ReplayFolder, ReplayFileManager.GetReplaySlotName(slot)));

    public static bool LoadReplay(string path)
    {
        var newReplay = ReplayFileManager.ReadReplay(path);
        if (newReplay == null) return false;

        if (LoadedReplay != null)
            Marshal.FreeHGlobal((nint)LoadedReplay);

        LoadedReplay = newReplay;
        
        ContentsReplayModule.Instance()->ReplayHeader   = LoadedReplay->ReplayHeader;
        ContentsReplayModule.Instance()->ReplayChapters = LoadedReplay->ReplayChapters;
        ContentsReplayModule.Instance()->DataLoadType   = 0;

        Service.Config.LastLoadedReplay = path;
        return true;
    }

    public static bool UnloadReplay()
    {
        if (LoadedReplay == null) return false;

        Marshal.FreeHGlobal((nint)LoadedReplay);
        LoadedReplay = null;
        return true;
    }

    public static void JumpToChapter(byte chapter)
    {
        var jumpChapter = ContentsReplayModule.Instance()->ReplayChapters[chapter];
        if (jumpChapter == null) return;

        ContentsReplayModule.Instance()->OverallDataOffset = jumpChapter->Offset;
        ContentsReplayModule.Instance()->Seek              = jumpChapter->MS / 1000f;
    }

    public static void JumpToTime(uint ms)
    {
        var segment = LoadedReplay->FindNextDataSegment(ms, out var offset);
        if (segment == null) return;

        ContentsReplayModule.Instance()->OverallDataOffset = offset;
        ContentsReplayModule.Instance()->Seek              = segment->MS / 1000f;
    }

    public static void JumpToTimeBeforeChapter(byte chapter, uint ms)
    {
        var jumpChapter = ContentsReplayModule.Instance()->ReplayChapters[chapter];
        if (jumpChapter == null) return;

        JumpToTime(jumpChapter->MS > ms ? jumpChapter->MS - ms : 0);
    }

    public static void SeekToTime(uint ms)
    {
        if (ContentsReplayModule.Instance()->IsLoadingChapter) return;

        var prevChapter = ContentsReplayModule.Instance()->ReplayChapters.FindPreviousChapterFromTime(ms);
        var segment     = LoadedReplay->FindNextDataSegment(ms, out var offset);
        if (segment == null) return;

        SeekingOffset = offset;
        ForceFastForwardPatch.Enable();
        if ((int)(ContentsReplayModule.Instance()->Seek * 1000) < segment->MS && prevChapter == ContentsReplayModule.GetCurrentChapter())
            OnSetChapterHook.Original(ContentsReplayModule.Instance(), prevChapter);
        else
            ContentsReplayModule.Instance()->SetChapter(prevChapter);
    }

    public static void ReplaySection(byte from, byte to)
    {
        if (from != 0 && ContentsReplayModule.Instance()->OverallDataOffset < ContentsReplayModule.Instance()->ReplayChapters[from]->Offset)
            JumpToChapter(from);

        SeekingChapter = to;
        if (SeekingChapter >= QuickLoadChapter)
            QuickLoadChapter = 0;
    }

    public static void DoQuickLoad()
    {
        if (SeekingChapter == 0)
        {
            ReplaySection(0, 1);
            return;
        }

        var nextEvent = ContentsReplayModule.Instance()->ReplayChapters.FindNextChapterType(SeekingChapter, 4);
        if (nextEvent != 0 && nextEvent < QuickLoadChapter - 1)
        {
            var nextCountdown = ContentsReplayModule.Instance()->ReplayChapters.FindNextChapterType(nextEvent, 1);
            if (nextCountdown == 0 || nextCountdown > nextEvent + 2)
                nextCountdown = (byte)(nextEvent + 2);
            ReplaySection(nextEvent, nextCountdown);
            return;
        }

        for (var i = 0; i < 100; i++)
        {
            var o = CharacterManager.Instance()->BattleCharas[i].Value;
            if (o != null && o->Character.GameObject.GetObjectKind() == ObjectKind.BattleNpc)
                DeleteCharacterAtIndex(i);
        }

        JumpToTimeBeforeChapter(ContentsReplayModule.Instance()->ReplayChapters.FindPreviousChapterType(QuickLoadChapter, 2), 15_000);
        ReplaySection(0, QuickLoadChapter);
    }
    
    #endregion
}

