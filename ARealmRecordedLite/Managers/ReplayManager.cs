using System.IO;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using MemoryPatch = ARealmRecordedLite.Utilities.MemoryPatch;

namespace ARealmRecordedLite.Managers;

public static unsafe class ReplayManager
{
    private static FFXIVReplay* loadedReplay;
    private static byte         quickLoadChapter;
    private static byte         seekingChapter;
    private static uint         seekingOffset;

    private static readonly MemoryPatch removeProcessingLimitPatch =
        new("41 FF C4 48 39 43 38", [0x90, 0x90, 0x90]);

    private static readonly MemoryPatch removeProcessingLimitPatch2 =
        new("0F 87 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? 33 F6", [0x90, 0x90, 0x90, 0x90, 0x90, 0x90]);

    private static readonly MemoryPatch forceFastForwardPatch =
        new("0F 83 ?? ?? ?? ?? 41 0F B7 46 02 4D 8D 46 0C", [0x90, 0x90, 0x90, 0x90, 0x90, 0x90]);

    public static void Init()
    {
        removeProcessingLimitPatch.Enable();
        // ignored
    }

    public static void Uninit()
    {
        removeProcessingLimitPatch.Disable();
        removeProcessingLimitPatch2.Disable();
        forceFastForwardPatch.Disable();

        if (loadedReplay == null) return;

        if (ContentsReplayModule.Instance()->InPlayback)
        {
            ContentsReplayModule.Instance()->playbackControls |= 8;
            Service.Log.Error("插件已卸载, 若不继续重新加载插件或录像可能导致文件损毁");
        }

        Marshal.FreeHGlobal((nint)loadedReplay);
        loadedReplay = null;
    }


    public static void PlaybackUpdate(ContentsReplayModule* contentsReplayModule)
    {
        if (loadedReplay == null) return;

        contentsReplayModule->dataLoadType = 0;
        contentsReplayModule->dataOffset   = 0;

        if (quickLoadChapter < 2) return;

        var seekedTime = contentsReplayModule->chapters[seekingChapter]->ms;
        if (seekedTime > (int)(contentsReplayModule->Seek * 1000)) return;

        DoQuickLoad();
    }

    public static FFXIVReplay.DataSegment* GetReplayDataSegment(ContentsReplayModule* contentsReplayModule)
    {
        if (loadedReplay == null) return null;

        if (seekingOffset > 0 && seekingOffset <= contentsReplayModule->OverallDataOffset)
        {
            forceFastForwardPatch.Disable();
            seekingOffset = 0;
        }

        if (Service.Config.MaxSeekDelta <= 100 || contentsReplayModule->seekDelta >= Service.Config.MaxSeekDelta)
            removeProcessingLimitPatch2.Disable();
        else
            removeProcessingLimitPatch2.Enable();

        return loadedReplay->GetDataSegment((uint)contentsReplayModule->OverallDataOffset);
    }

    public static void OnSetChapter(ContentsReplayModule* contentsReplayModule, byte chapter)
    {
        if (!Service.Config.EnableQuickLoad                    ||
            chapter                                      <= 0 ||
            contentsReplayModule->chapters.length        < 2  ||
            ContentsReplayModule.GetCurrentChapter() + 1 == chapter)
            return;

        quickLoadChapter = chapter;
        seekingChapter   = 0;
        DoQuickLoad();
    }

    public static bool LoadReplay(int slot) => LoadReplay(Path.Combine(CoreManager.ReplayFolder, CoreManager.GetReplaySlotName(slot)));

    public static bool LoadReplay(string path)
    {
        var newReplay = CoreManager.ReadReplay(path);
        if (newReplay == null) return false;

        if (loadedReplay != null)
            Marshal.FreeHGlobal((nint)loadedReplay);

        loadedReplay                                  = newReplay;
        ContentsReplayModule.Instance()->replayHeader = loadedReplay->header;
        ContentsReplayModule.Instance()->chapters     = loadedReplay->chapters;
        ContentsReplayModule.Instance()->dataLoadType = 0;

        Service.Config.LastLoadedReplay = path;
        return true;
    }

    public static bool UnloadReplay()
    {
        if (loadedReplay == null) return false;

        Marshal.FreeHGlobal((nint)loadedReplay);
        loadedReplay = null;
        return true;
    }

    public static void JumpToChapter(byte chapter)
    {
        var jumpChapter = ContentsReplayModule.Instance()->chapters[chapter];
        if (jumpChapter == null) return;

        ContentsReplayModule.Instance()->OverallDataOffset = jumpChapter->offset;
        ContentsReplayModule.Instance()->Seek              = jumpChapter->ms / 1000f;
    }

    public static void JumpToTime(uint ms)
    {
        var segment = loadedReplay->FindNextDataSegment(ms, out var offset);
        if (segment == null) return;

        ContentsReplayModule.Instance()->OverallDataOffset = offset;
        ContentsReplayModule.Instance()->Seek              = segment->ms / 1000f;
    }

    public static void JumpToTimeBeforeChapter(byte chapter, uint ms)
    {
        var jumpChapter = ContentsReplayModule.Instance()->chapters[chapter];
        if (jumpChapter == null) return;

        JumpToTime(jumpChapter->ms > ms ? jumpChapter->ms - ms : 0);
    }

    public static void SeekToTime(uint ms)
    {
        if (ContentsReplayModule.Instance()->IsLoadingChapter) return;

        var prevChapter = ContentsReplayModule.Instance()->chapters.FindPreviousChapterFromTime(ms);
        var segment     = loadedReplay->FindNextDataSegment(ms, out var offset);
        if (segment == null) return;

        seekingOffset = offset;
        forceFastForwardPatch.Enable();
        if ((int)(ContentsReplayModule.Instance()->Seek * 1000) < segment->ms &&
            prevChapter                                         == ContentsReplayModule.GetCurrentChapter())
            ContentsReplayModule.Instance()->OnSetChapter(prevChapter);
        else
            ContentsReplayModule.Instance()->SetChapter(prevChapter);
    }

    public static void ReplaySection(byte from, byte to)
    {
        if (from != 0 && ContentsReplayModule.Instance()->OverallDataOffset < ContentsReplayModule.Instance()->chapters[from]->offset)
            JumpToChapter(from);

        seekingChapter = to;
        if (seekingChapter >= quickLoadChapter)
            quickLoadChapter = 0;
    }

    public static void DoQuickLoad()
    {
        if (seekingChapter == 0)
        {
            ReplaySection(0, 1);
            return;
        }

        var nextEvent = ContentsReplayModule.Instance()->chapters.FindNextChapterType(seekingChapter, 4);
        if (nextEvent != 0 && nextEvent < quickLoadChapter - 1)
        {
            var nextCountdown = ContentsReplayModule.Instance()->chapters.FindNextChapterType(nextEvent, 1);
            if (nextCountdown == 0 || nextCountdown > nextEvent + 2)
                nextCountdown = (byte)(nextEvent + 2);
            ReplaySection(nextEvent, nextCountdown);
            return;
        }

        for (var i = 0; i < 100; i++)
        {
            var o = CharacterManager.Instance()->BattleCharas[i].Value;
            if (o != null && o->Character.GameObject.GetObjectKind() == ObjectKind.BattleNpc)
                CoreManager.DeleteCharacterAtIndex(i);
        }

        JumpToTimeBeforeChapter(ContentsReplayModule.Instance()->chapters.FindPreviousChapterType(quickLoadChapter, 2), 15_000);
        ReplaySection(0, quickLoadChapter);
    }
}

