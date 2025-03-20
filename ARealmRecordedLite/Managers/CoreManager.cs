using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Config;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using Lumina.Excel.Sheets;
using CompSig = ARealmRecordedLite.Utilities.CompSig;
using MemoryPatch = ARealmRecordedLite.Utilities.MemoryPatch;

namespace ARealmRecordedLite.Managers;

public unsafe class CoreManager
{

    #region MemoryPatch

    private static MemoryPatch? CreateGetReplaySegmentHookPatch;

    private static readonly MemoryPatch RemoveRecordReadyToastPatch =
        new("BA CB 07 00 00 48 8B CF E8", [0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90]);

    private static readonly MemoryPatch AlwaysRecordPatch =
        new("24 06 3C 02 75 23 48", [0xEB, 0x1F]);

    private static readonly MemoryPatch SeIsABunchOfClownsPatch =
        new("F6 40 78 02 74 04 B0 01 EB 02 32 C0 40 84 FF", [0x90, 0x90, 0x90, 0x90, 0x90, 0x90]);

    private static readonly MemoryPatch InstantFadeOutPatch =
        new("44 8D 47 0A 33 D2", [null, null, 0x07, 0x90]);

    private static readonly MemoryPatch InstantFadeInPatch =
        new("44 8D 42 0A 41 FF 92 ?? ?? 00 00 48 8B 5C 24", [null, null, null, 0x01]);

    public static readonly MemoryPatch ReplaceLocalPlayerNamePatch =
        new("75 ?? 48 8D 4C 24 ?? E8 ?? ?? ?? ?? F6 05", [0x90, 0x90], Service.Config.EnableHideOwnName);

    #endregion

    #region Hook & Delagte Definitions

    private static Hook<ContentsReplayModule.OnZoneInPacketDelegate>?            GetZoneInPacketHook;
    private static Hook<ContentsReplayModule.InitializeRecordingDelegate>?       InitializeRecordingHook;
    private static Hook<ContentsReplayModule.RequestPlaybackDelegate>?           RequestPlaybackHook;
    private static Hook<ContentsReplayModule.ReceiveActorControlPacketDelegate>? ReceiveActorControlPacketHook;
    private static Hook<ContentsReplayModule.OnSetChapterDelegate>?              OnSetChapterHook;
    private static Hook<ContentsReplayModule.ReplayPacketDelegate>?              ReplayPacketHook;

    private static readonly CompSig FormatAddonTextTimestampSig = new("E8 ?? ?? ?? ?? 8D 4D 64");
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

    private static readonly CompSig ContentDirectorOffsetSig = new("F6 81 7C 0D 00 00 01 75 74");
    private static          short   contentDirectorOffset;

    private static readonly CompSig DisplaySelectedDutyRecordingSig = new("E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? 39 6E 34");
    private delegate void DisplaySelectedDutyRecordingDelegate(nint a1);
    private static DisplaySelectedDutyRecordingDelegate? displaySelectedDutyRecording;

    private static readonly CompSig DeleteCharacterAtIndexSig = new("E8 ?? ?? ?? ?? 33 D2 48 8D 47 50");
    private delegate void DeleteCharacterAtIndexDelegate(CharacterManager* manager, int index);
    private static DeleteCharacterAtIndexDelegate? deleteCharacterAtIndex;

    public static readonly CompSig                             GetReplayDataSegmentSig = new("40 53 48 83 EC 20 F6 81 2A 07 00 00 04 48 8B D9 0F 85 ?? ?? ?? ??");
    public delegate        FFXIVReplay.DataSegment*            GetReplayDataSegmentDelegate(ContentsReplayModule* contentsReplayModule);
    private static         Hook<GetReplayDataSegmentDelegate>? GetReplayDataSegmentHook;

    public static readonly CompSig PlaybackUpdateSig = new("48 8B C4 53 48 81 EC ?? ?? ?? ?? F6 81 ?? ?? ?? ?? ?? 48 8B D9 0F 84 ?? ?? ?? ?? 48 89 68");
    public delegate        void PlaybackUpdateDelegate(ContentsReplayModule* contentsReplayModule);
    private static         Hook<PlaybackUpdateDelegate>? PlaybackUpdateHook;
    
    public static readonly CompSig                       ExecuteCommandSig = new("E8 ?? ?? ?? ?? 48 8B 06 48 8B CE FF 50 ?? E9 ?? ?? ?? ?? 49 8B CC");
    private delegate       nint                          ExecuteCommandDelegate(int command, int param1, int param2, int param3, int param4);
    private static         Hook<ExecuteCommandDelegate>? ExecuteCommandHook;

    #endregion

    public static string ReplayFolder      => Path.Combine(Framework.Instance()->UserPathString, "replay");
    public static string AutoRenamedFolder => Path.Combine(ReplayFolder,                         "autorenamed");
    public static string ArchiveZip        => Path.Combine(ReplayFolder,                         "archive.zip");
    public static string DeletedFolder     => Path.Combine(ReplayFolder,                         "deleted");

    private static readonly HashSet<int> FlagsToIgnore = [201, 1981];

    private static readonly HashSet<uint> WhitelistedContentTypes = [1, 2, 3, 4, 5, 9, 28, 29, 30, 37];

    internal static void Init()
    {
        ReplayManager.Init();

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
        CreateGetReplaySegmentHookPatch.Enable();
        RemoveRecordReadyToastPatch.Enable();
        AlwaysRecordPatch.Enable();
        SeIsABunchOfClownsPatch.Enable();
        InstantFadeOutPatch.Enable();
        InstantFadeOutPatch.Enable();
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

        OnSetChapterHook ??= ContentsReplayModule.OnSetChapterSig.GetHook<ContentsReplayModule.OnSetChapterDelegate>(OnSetChapterDetour);
        OnSetChapterHook.Enable();

        ReplayPacketHook ??= ContentsReplayModule.ReplayPacketSig.GetHook<ContentsReplayModule.ReplayPacketDelegate>(ReplayPacketDetour);
        ReplayPacketHook.Enable();

        FormatAddonTextTimestampHook ??= FormatAddonTextTimestampSig.GetHook<FormatAddonTextTimestampDelegate>(FormatAddonTextTimestampDetour);
        FormatAddonTextTimestampHook.Enable();

        ExecuteCommandHook ??= ExecuteCommandSig.GetHook<ExecuteCommandDelegate>(OnPreExecuteCommand);
        ExecuteCommandHook.Enable();

        ContentsReplayModule.SetSavedReplayCIDs(Service.ClientState.LocalContentId);

        if (ContentsReplayModule.Instance()->InPlayback && ContentsReplayModule.Instance()->fileStream != nint.Zero &&
            *(long*)ContentsReplayModule.Instance()->fileStream                                        == 0)
            ReplayManager.LoadReplay(Service.Config.LastLoadedReplay);
    }

    internal static void Uninit()
    {
        RemoveRecordReadyToastPatch.Disable();
        AlwaysRecordPatch.Disable();
        SeIsABunchOfClownsPatch.Disable();
        InstantFadeOutPatch.Disable();
        InstantFadeOutPatch.Disable();
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

        if (ContentsReplayModule.Instance() != null)
            ContentsReplayModule.SetSavedReplayCIDs(0);

        ReplayManager.Uninit();
    }

    #region Hook Detour

    private static void OnZoneInPacketDetour(ContentsReplayModule* contentsReplayModule, uint gameObjectID, nint packet)
    {
        GetZoneInPacketHook.Original(contentsReplayModule, gameObjectID, packet);

        if ((contentsReplayModule->status & 1) == 0) return;

        if (Service.GameConfig.UiConfig.TryGetBool(nameof(UiConfigOption.CutsceneSkipIsContents), out var b) && b)
            InitializeRecordingDetour(contentsReplayModule);
    }

    private static void InitializeRecordingDetour(ContentsReplayModule* contentsReplayModule)
    {
        var id = contentsReplayModule->initZonePacket.contentFinderCondition;
        if (id == 0) return;

        if (!Service.Data.GetExcelSheet<ContentFinderCondition>().TryGetRow(id, out var contentFinderCondition)) return;

        var contentType = contentFinderCondition.ContentType.RowId;
        if (!WhitelistedContentTypes.Contains(contentType)) return;

        contentsReplayModule->FixNextReplaySaveSlot(Service.Config.MaxAutoRenamedReplays);
        InitializeRecordingHook.Original(contentsReplayModule);
        contentsReplayModule->BeginRecording();

        var header = contentsReplayModule->replayHeader;
        header.localCID                    = 0;
        contentsReplayModule->replayHeader = header;

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
            prevHeader                                  = contentsReplayModule->savedReplayHeaders[0];
            contentsReplayModule->savedReplayHeaders[0] = lastSelectedHeader;
        }
        else
            LastSelectedReplay = null;

        var ret = RequestPlaybackHook.Original(contentsReplayModule, slot);

        if (customSlot)
            contentsReplayModule->savedReplayHeaders[0] = prevHeader;

        return ret;
    }

    private static void ReceiveActorControlPacketDetour(ContentsReplayModule* contentsReplayModule, uint gameObjectID, nint packet)
    {
        ReceiveActorControlPacketHook.Original(contentsReplayModule, gameObjectID, packet);
        if (*(ushort*)packet != 931 || !*(bool*)(packet + 4)) return;

        ReplayManager.UnloadReplay();

        if (string.IsNullOrEmpty(LastSelectedReplay))
            ReplayManager.LoadReplay(contentsReplayModule->currentReplaySlot);
        else
            ReplayManager.LoadReplay(LastSelectedReplay);
    }

    private static void PlaybackUpdateDetour(ContentsReplayModule* contentsReplayModule)
    {
        GetReplayDataSegmentHook?.Enable();
        PlaybackUpdateHook.Original(contentsReplayModule);
        GetReplayDataSegmentHook?.Disable();

        UpdateAutoRename();

        if (contentsReplayModule->IsRecording &&
            contentsReplayModule->chapters[0]->type == 1)
            contentsReplayModule->chapters[0]->type = 5;

        if (!contentsReplayModule->InPlayback) return;

        SetConditionFlag(ConditionFlag.OccupiedInCutSceneEvent, false);

        ReplayManager.PlaybackUpdate(contentsReplayModule);
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

            var currentChapterMS = ContentsReplayModule.Instance()->chapters[a3 - 1]->ms;
            var nextChapterMS    = a3 < 64 ? ContentsReplayModule.Instance()->chapters[a3]->ms : ContentsReplayModule.Instance()->replayHeader.totalMS;
            if (nextChapterMS < currentChapterMS)
                nextChapterMS = ContentsReplayModule.Instance()->replayHeader.totalMS;

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
            ContentsReplayModule.Instance()->status |= 64;
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

    #endregion

    #region Event

    private static nint OnPreExecuteCommand(int command, int param1, int param2, int param3, int param4)
    {
        if (!ContentsReplayModule.Instance()->InPlayback ||
            FlagsToIgnore.Contains(command))
            return ExecuteCommandHook.Original(command, param1, param2, param3, param4);

        if (command == 314)
            SetConditionFlag(ConditionFlag.WatchingCutscene, param1 != 0);

        return nint.Zero;
    }

    #endregion

    #region Delegate

    public static bool IsWaymarkVisible => (*waymarkToggle & 2) == 0;

    public static void DisplaySelectedDutyRecording(nint agent) => displaySelectedDutyRecording(agent);

    public static void DeleteCharacterAtIndex(int i) => deleteCharacterAtIndex(CharacterManager.Instance(), i);

    #endregion

    private static readonly Regex bannedFileCharacters = new("[\\\\\\/:\\*\\?\"\\<\\>\\|\u0000-\u001F]");

    private static List<(FileInfo, FFXIVReplay)>? replayList;

    public static List<(FileInfo, FFXIVReplay)> ReplayList
    {
        get => replayList ?? GetReplayList();
        set => replayList = value;
    }

    public static  string?            LastSelectedReplay { get; set; }
    private static FFXIVReplay.Header lastSelectedHeader;

    private static bool WasRecording;

    public static string GetReplaySlotName(int slot) => $"FFXIV_{Service.ClientState.LocalContentId:X16}_{slot:D3}.dat";

    private static void UpdateAutoRename()
    {
        switch (ContentsReplayModule.Instance()->IsRecording)
        {
            case true when !WasRecording:
                WasRecording = true;
                break;
            case false when WasRecording:
                WasRecording = false;
                Service.Framework.RunOnTick(() =>
                {
                    AutoRenameReplay();
                    ContentsReplayModule.SetSavedReplayCIDs(Service.ClientState.LocalContentId);
                }, default, 30);
                break;
        }
    }

    public static FFXIVReplay* ReadReplay(string path)
    {
        var ptr       = nint.Zero;
        var allocated = false;

        try
        {
            using var fs = File.OpenRead(path);

            ptr       = Marshal.AllocHGlobal((int)fs.Length);
            allocated = true;

            _ = fs.Read(new Span<byte>((void*)ptr, (int)fs.Length));
        }
        catch (Exception e)
        {
            Service.Log.Error(e, $"Failed to read replay {path}");

            if (allocated)
            {
                Marshal.FreeHGlobal(ptr);
                ptr = nint.Zero;
            }
        }

        return (FFXIVReplay*)ptr;
    }

    public static FFXIVReplay? ReadReplayHeaderAndChapters(string path)
    {
        try
        {
            using var fs    = File.OpenRead(path);
            var       size  = sizeof(FFXIVReplay.Header) + sizeof(FFXIVReplay.ChapterArray);
            var       bytes = new byte[size];
            if (fs.Read(bytes, 0, size) != size)
                return null;
            fixed (byte* ptr = bytes) { return *(FFXIVReplay*)ptr; }
        }
        catch (Exception e)
        {
            Service.Log.Error(e, $"Failed to read replay header and chapters {path}");
            return null;
        }
    }

    public static List<(FileInfo, FFXIVReplay)> GetReplayList()
    {
        try
        {
            var directory = new DirectoryInfo(ReplayFolder);

            var renamedDirectory = new DirectoryInfo(AutoRenamedFolder);
            if (!renamedDirectory.Exists)
            {
                if (Service.Config.MaxAutoRenamedReplays > 0)
                    renamedDirectory.Create();
                else
                    renamedDirectory = null;
            }

            var list = (from file in directory.GetFiles().Concat(renamedDirectory?.GetFiles() ?? [])
                        where file.Extension == ".dat"
                        let replay = ReadReplayHeaderAndChapters(file.FullName)
                        where replay is { header.IsValid: true }
                        select (file, replay.Value)
                       ).ToList();

            replayList = list;
        }
        catch { replayList = []; }

        return replayList;
    }

    public static void RenameReplay(FileInfo file, string name)
    {
        try
        {
            file.MoveTo(Path.Combine(ReplayFolder, $"{name}.dat"));
        }
        catch (Exception e)
        {
            Service.Log.Error(e, "重命名录像失败");
        }
    }

    public static void AutoRenameReplay()
    {
        if (Service.Config.MaxAutoRenamedReplays <= 0)
        {
            GetReplayList();
            return;
        }

        try
        {
            var (file, replay) = GetReplayList().Where(t => t.Item1.Name.StartsWith("FFXIV_")).MaxBy(t => t.Item1.LastWriteTime);

            var name =
                $"{bannedFileCharacters.Replace(ContentsReplayModule.Instance()->contentTitle.ToString(), string.Empty)} {DateTime.Now:yyyy.MM.dd HH.mm.ss}";
            file.MoveTo(Path.Combine(AutoRenamedFolder, $"{name}.dat"));

            var renamedFiles = new DirectoryInfo(AutoRenamedFolder).GetFiles().Where(f => f.Extension == ".dat").ToList();
            while (renamedFiles.Count > Service.Config.MaxAutoRenamedReplays)
            {
                DeleteReplay(renamedFiles.OrderBy(f => f.CreationTime).First());
                renamedFiles = new DirectoryInfo(AutoRenamedFolder).GetFiles().Where(f => f.Extension == ".dat").ToList();
            }

            GetReplayList();

            for (var i = 0; i < 3; i++)
            {
                if (ContentsReplayModule.Instance()->savedReplayHeaders[i].timestamp != replay.header.timestamp) continue;
                ContentsReplayModule.Instance()->savedReplayHeaders[i] = new FFXIVReplay.Header();
                break;
            }
        }
        catch (Exception e)
        {
            Service.Log.Error(e, "重命名录像失败");
        }
    }

    public static void DeleteReplay(FileInfo file)
    {
        try
        {
            if (Service.Config.MaxDeletedReplays > 0)
            {
                var deletedDirectory = new DirectoryInfo(DeletedFolder);
                if (!deletedDirectory.Exists)
                    deletedDirectory.Create();

                file.MoveTo(Path.Combine(DeletedFolder, file.Name), true);

                var deletedFiles = deletedDirectory.GetFiles().Where(f => f.Extension == ".dat").ToList();
                while (deletedFiles.Count > Service.Config.MaxDeletedReplays)
                {
                    deletedFiles.OrderBy(f => f.CreationTime).First().Delete();
                    deletedFiles = deletedDirectory.GetFiles().Where(f => f.Extension == ".dat").ToList();
                }
            }
            else
                file.Delete();


            GetReplayList();
        }
        catch (Exception e)
        {
            Service.Log.Error(e, "删除录像失败");
        }
    }

    public static void ArchiveReplays()
    {
        var archivableReplays = ReplayList.Where(t => !t.Item2.header.IsPlayable && t.Item1.Directory?.Name == "replay").ToArray();
        if (archivableReplays.Length == 0) return;

        var restoreBackup = true;

        try
        {
            using (var zipFileStream = new FileStream(ArchiveZip, FileMode.OpenOrCreate))
            using (var zipFile = new ZipArchive(zipFileStream, ZipArchiveMode.Update))
            {
                var expectedEntryCount = zipFile.Entries.Count;
                if (expectedEntryCount > 0)
                {
                    var prevPosition = zipFileStream.Position;
                    zipFileStream.Position = 0;
                    using var zipBackupFileStream = new FileStream($"{ArchiveZip}.BACKUP", FileMode.Create);
                    zipFileStream.CopyTo(zipBackupFileStream);
                    zipFileStream.Position = prevPosition;
                }

                foreach (var (file, _) in archivableReplays)
                {
                    zipFile.CreateEntryFromFile(file.FullName, file.Name);
                    expectedEntryCount++;
                }

                if (zipFile.Entries.Count != expectedEntryCount)
                    throw new IOException(
                        $"Number of archived replays was unexpected (Expected: {expectedEntryCount}, Actual: {zipFile.Entries.Count}) after archiving, restoring backup!");
            }

            restoreBackup = false;

            foreach (var (file, _) in archivableReplays)
                file.Delete();
        }
        catch (Exception e)
        {
            if (restoreBackup)
                try
                {
                    using var zipBackupFileStream = new FileStream($"{ArchiveZip}.BACKUP", FileMode.Open);
                    using var zipFileStream       = new FileStream(ArchiveZip,             FileMode.Create);
                    zipBackupFileStream.CopyTo(zipFileStream);
                }
                catch
                {
                    // ignored
                }

            Service.Log.Error(e, "尝试归档录像时发生错误");
        }

        GetReplayList();
    }

    public static void SetDutyRecorderMenuSelection(nint agent, byte slot)
    {
        *(byte*)(agent + 0x2C) = slot;
        *(byte*)(agent + 0x2A) = 1;
        DisplaySelectedDutyRecording(agent);
    }

    public static void SetDutyRecorderMenuSelection(nint agent, string path, FFXIVReplay.Header header)
    {
        header.localCID    = Service.ClientState.LocalContentId;
        LastSelectedReplay = path;
        lastSelectedHeader = header;

        var prevHeader = ContentsReplayModule.Instance()->savedReplayHeaders[0];
        ContentsReplayModule.Instance()->savedReplayHeaders[0] = header;

        SetDutyRecorderMenuSelection(agent, 0);
        ContentsReplayModule.Instance()->savedReplayHeaders[0] = prevHeader;

        *(byte*)(agent + 0x2C) = 100;
    }

    public static void CopyReplayIntoSlot(nint agent, FileInfo file, FFXIVReplay.Header header, byte slot)
    {
        if (slot > 2) return;

        try
        {
            file.CopyTo(Path.Combine(ReplayFolder, GetReplaySlotName(slot)), true);

            header.localCID = Service.ClientState.LocalContentId;

            ContentsReplayModule.Instance()->savedReplayHeaders[slot] = header;
            SetDutyRecorderMenuSelection(agent, slot);
            GetReplayList();
        }
        catch (Exception e)
        {
            Service.Log.Error(e, $"将录像复制到第 {slot + 1} 槽时发生错误");
        }
    }

    public static void OpenReplayFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = ReplayFolder,
                UseShellExecute = true
            });
        }
        catch
        {
            // ignored
        }
    }

    public static void ToggleWaymarks() => *waymarkToggle ^= 2;

    public static void SetConditionFlag(ConditionFlag flag, bool b) => *(bool*)(Service.Condition.Address + (int)flag) = b;

    public static string ReadCString(nint address) => Marshal.PtrToStringUTF8(address);

    public static string ReadCString(nint address, int len) => Marshal.PtrToStringUTF8(address, len);

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
}
