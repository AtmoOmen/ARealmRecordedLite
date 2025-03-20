using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using ARealmRecordedLite.Managers;
using FFXIVClientStructs.FFXIV.Client.System.String;
using Lumina.Excel.Sheets;
using CompSig = ARealmRecordedLite.Utilities.CompSig;

namespace ARealmRecordedLite;

[StructLayout(LayoutKind.Explicit, Size = 0x730)]
public unsafe struct ContentsReplayModule
{
    private static readonly CompSig               InstanceSig = new("48 8D 0D ?? ?? ?? ?? 0F B6 D8 E8 ?? ?? ?? ?? 44 0F B6 C0");
    private static          ContentsReplayModule* instance;

    public static ContentsReplayModule* Instance()
    {
        if (instance == null)
            instance = InstanceSig.GetStatic<ContentsReplayModule>();
        return instance;
    }
    
    public static void SetSavedReplayCIDs(ulong cID)
    {
        if (Instance()->savedReplayHeaders == null) return;

        for (var i = 0; i < 3; i++)
        {
            var header = Instance()->savedReplayHeaders[i];
            if (!header.IsValid) continue;
            header.localCID                   = cID;
            Instance()->savedReplayHeaders[i] = header;
        }
    }
    
    public static byte GetCurrentChapter() => Instance()->chapters.FindPreviousChapterFromTime((uint)(Instance()->Seek * 1000));


    [StructLayout(LayoutKind.Explicit, Size = 0x70)]
    public struct InitZonePacket
    {
        [FieldOffset(0x0)] public ushort u0x0;
        [FieldOffset(0x2)] public ushort territoryType;
        [FieldOffset(0x4)] public ushort u0x4;
        [FieldOffset(0x6)] public ushort contentFinderCondition;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0xC0)]
    public struct UnknownPacket;

    [FieldOffset(0x0)]   public int                      gameBuildNumber;
    [FieldOffset(0x8)]   public nint                     fileStream;
    [FieldOffset(0x10)]  public nint                     fileStreamNextWrite;
    [FieldOffset(0x18)]  public nint                     fileStreamEnd;
    [FieldOffset(0x20)]  public long                     u0x20;
    [FieldOffset(0x28)]  public long                     u0x28;
    [FieldOffset(0x30)]  public long                     dataOffset;
    [FieldOffset(0x38)]  public long                     OverallDataOffset;
    [FieldOffset(0x40)]  public long                     lastDataOffset;
    [FieldOffset(0x48)]  public FFXIVReplay.Header       replayHeader;
    [FieldOffset(0xB0)]  public FFXIVReplay.ChapterArray chapters;
    [FieldOffset(0x3B8)] public Utf8String               contentTitle;
    [FieldOffset(0x420)] public long                     nextDataSection;
    [FieldOffset(0x428)] public long                     numberBytesRead;
    [FieldOffset(0x430)] public int                      currentFileSection;
    [FieldOffset(0x434)] public int                      dataLoadType;
    [FieldOffset(0x438)] public long                     dataLoadOffset;
    [FieldOffset(0x440)] public long                     dataLoadLength;
    [FieldOffset(0x448)] public long                     dataLoadFileOffset;
    [FieldOffset(0x450)] public long                     localCID;
    [FieldOffset(0x458)] public byte                     currentReplaySlot;
    [FieldOffset(0x460)] public Utf8String               characterRecordingName;
    [FieldOffset(0x4C8)] public Utf8String               replayTitle;
    [FieldOffset(0x530)] public Utf8String               u0x530;
    [FieldOffset(0x598)] public float                    recordingTime;
    [FieldOffset(0x5A0)] public long                     recordingLength;
    [FieldOffset(0x5A8)] public int                      u0x5A8;
    [FieldOffset(0x5AC)] public byte                     u0x5AC;
    [FieldOffset(0x5AD)] public byte                     nextReplaySaveSlot;
    [FieldOffset(0x5B0)] public FFXIVReplay.Header*      savedReplayHeaders;
    [FieldOffset(0x5B8)] public nint                     u0x5B8;
    [FieldOffset(0x5C0)] public nint                     u0x5C0;
    [FieldOffset(0x5C8)] public byte                     u0x5C8;
    [FieldOffset(0x5CC)] public uint                     localPlayerObjectID;
    [FieldOffset(0x5D0)] public InitZonePacket           initZonePacket;
    [FieldOffset(0x640)] public long                     u0x640;
    [FieldOffset(0x648)] public UnknownPacket            u0x648;
    [FieldOffset(0x708)] public int                      u0x708;
    [FieldOffset(0x70C)] public float                    Seek;
    [FieldOffset(0x710)] public float                    seekDelta;
    [FieldOffset(0x714)] public float                    Speed;
    [FieldOffset(0x718)] public float                    u0x718;
    [FieldOffset(0x71C)] public byte                     selectedChapter;
    [FieldOffset(0x720)] public uint                     startingMS;
    [FieldOffset(0x724)] public int                      u0x724;
    [FieldOffset(0x728)] public short                    u0x728;
    [FieldOffset(0x72A)] public byte                     status;
    [FieldOffset(0x72B)] public byte                     playbackControls;
    [FieldOffset(0x72C)] public byte                     u0x72C;

    public bool InPlayback       => (playbackControls & 4)    != 0;
    public bool IsPaused         => (playbackControls & 8)    != 0;
    public bool IsSavingPackets  => (status           & 4)    != 0;
    public bool IsRecording      => (status           & 0x74) == 0x74;
    public bool IsLoadingChapter => selectedChapter           < 0x40;
    
    public void FixNextReplaySaveSlot(int maxAutoRenamedReplays)
    {
        if (maxAutoRenamedReplays <= 0 && !savedReplayHeaders[nextReplaySaveSlot].IsLocked) return;

        for (byte i = 0; i < 3; i++)
        {
            if (i != 2)
            {
                var header = savedReplayHeaders[i];
                if (header.IsLocked) continue;
            }

            nextReplaySaveSlot = i;
            return;
        }
    }
    

    public static readonly CompSig                 BeginRecordingSig = new("E8 ?? ?? ?? ?? 48 8B 5C 24 ?? 48 83 C4 20 41 5C");
    public delegate        void                    BeginRecordingDelegate(ContentsReplayModule* module, bool saveRecording);
    private static         BeginRecordingDelegate? beginRecording;
    
    public void BeginRecording(bool saveRecording = true)
    {
        if (beginRecording == null)
            beginRecording = BeginRecordingSig.GetDelegate<BeginRecordingDelegate>();

        fixed (ContentsReplayModule* ptr = &this)
            beginRecording.Invoke(ptr, saveRecording);
    }
    

    public static readonly CompSig               EndRecordingSig = new("E8 ?? ?? ?? ?? 32 C0 EB A3");
    public delegate        void                  EndRecordingDelegate(ContentsReplayModule* contentsReplayModule);
    private static         EndRecordingDelegate? endRecording;
    
    public void EndRecording()
    {
        if (endRecording == null)
            endRecording = EndRecordingSig.GetDelegate<EndRecordingDelegate>();
        fixed (ContentsReplayModule* ptr = &this)
            endRecording.Invoke(ptr);
    }


    public static readonly CompSig                 OnZoneInPacketSig = new("E8 ?? ?? ?? ?? 45 33 C0 48 8D 56 10 8B CF E8 ?? ?? ?? ?? 48 8D 4E 6C");
    public delegate        void                    OnZoneInPacketDelegate(ContentsReplayModule* contentsReplayModule, uint objectID, nint packet);
    private static         OnZoneInPacketDelegate? onZoneInPacket;

    public void OnZoneInPacket(uint objectID, nint packet)
    {
        if (onZoneInPacket == null)
            onZoneInPacket = OnZoneInPacketSig.GetDelegate<OnZoneInPacketDelegate>();
        
        fixed(ContentsReplayModule* ptr = &this)
            onZoneInPacket.Invoke(ptr, objectID, packet);
    }


    public static readonly CompSig                      InitializeRecordingSig = new("40 55 57 48 8D 6C 24 ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 27 F6 81");
    public delegate        void                         InitializeRecordingDelegate(ContentsReplayModule* contentsReplayModule);
    private static         InitializeRecordingDelegate? initializeRecording;

    public void InitializeRecording()
    {
        if (initializeRecording == null)
            initializeRecording = InitializeRecordingSig.GetDelegate<InitializeRecordingDelegate>();
        
        fixed (ContentsReplayModule* ptr = &this)
            initializeRecording.Invoke(ptr);
    }


    public static readonly CompSig                  RequestPlaybackSig = new("48 89 5C 24 08 57 48 83 EC 30 F6 81 ?? ?? ?? ?? 04");
    public delegate        bool                     RequestPlaybackDelegate(ContentsReplayModule* contentsReplayModule, byte slot);
    private static         RequestPlaybackDelegate? requestPlayback;

    public bool RequestPlayback(byte slot = 0)
    {
        if (requestPlayback == null)
            requestPlayback = RequestPlaybackSig.GetDelegate<RequestPlaybackDelegate>();
        
        fixed (ContentsReplayModule* ptr = &this)
            return requestPlayback.Invoke(ptr, slot);
    }

    
    public static readonly CompSig                            ReceiveActorControlPacketSig = new("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 41 56 41 57 48 83 EC 30 33 FF 48 8B D9");
    public delegate        void                               ReceiveActorControlPacketDelegate(ContentsReplayModule* contentsReplayModule, uint objectID, nint packet);
    private static         ReceiveActorControlPacketDelegate? receiveActorControlPacket;

    public void ReceiveActorControlPacket(uint objectID, nint packet)
    {
        if (receiveActorControlPacket == null)
            receiveActorControlPacket = ReceiveActorControlPacketSig.GetDelegate<ReceiveActorControlPacketDelegate>();

        fixed (ContentsReplayModule* ptr = &this)
            receiveActorControlPacket.Invoke(ptr, objectID, packet);
    }


    public static readonly CompSig                BeginPlaybackSig = new("40 53 48 83 EC 30 0F B6 81 ?? ?? ?? ?? 48 8B D9 A8 01 0F 84 ?? ?? ?? ?? 24 FE");
    public delegate        void                   BeginPlaybackDelegate(ContentsReplayModule* contentsReplayModule, bool allowed);
    private static         BeginPlaybackDelegate? beginPlayback;
    
    public void BeginPlayback(bool allowed = true)
    {
        if (beginPlayback == null)
            beginPlayback = BeginPlaybackSig.GetDelegate<BeginPlaybackDelegate>();
        
        fixed (ContentsReplayModule* ptr = &this)
            beginPlayback.Invoke(ptr, allowed);
    }
    
    
    public static readonly CompSig             SetChapterSig = new("E8 ?? ?? ?? ?? 84 C0 E9 ?? ?? ?? ?? 48 8D 4F 10");
    public delegate        bool                SetChapterDelegate(ContentsReplayModule* contentsReplayModule, byte chapter);
    private static         SetChapterDelegate? setChapter;

    public bool SetChapter(byte chapter)
    {
        if (setChapter == null)
            setChapter = SetChapterSig.GetDelegate<SetChapterDelegate>();
        
        fixed (ContentsReplayModule* ptr = &this)
            return setChapter.Invoke(ptr, chapter);
    }
    

    public static readonly CompSig OnSetChapterSig = new("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 48 89 7C 24 ?? 41 56 48 83 EC 30 48 8B F1 0F B6 EA");
    public delegate        void    OnSetChapterDelegate(ContentsReplayModule* contentsReplayModule, byte chapter);
    private static         OnSetChapterDelegate? onSetChapter;

    public void OnSetChapter(byte chapter)
    {
        if (onSetChapter == null)
            onSetChapter = OnSetChapterSig.GetDelegate<OnSetChapterDelegate>();
        
        fixed (ContentsReplayModule* ptr = &this)
            onSetChapter.Invoke(ptr, chapter);
    }


    public static readonly CompSig WritePacketSig = new("E8 ?? ?? ?? ?? 84 C0 74 60 33 C0");
    public delegate        bool    WritePacketDelegate(ContentsReplayModule* contentsReplayModule, uint objectID, ushort opcode, byte* data, ulong length);
    private static         WritePacketDelegate? writePacket;
    
    public bool WritePacket(uint objectID, ushort opcode, byte* data, ulong length)
    {
        if (writePacket == null)
            writePacket = WritePacketSig.GetDelegate<WritePacketDelegate>();
        
        fixed (ContentsReplayModule* ptr = &this)
            return writePacket.Invoke(ptr, objectID, opcode, data, length);
    }

    public bool WritePacket(uint objectID, ushort opcode, byte[] data)
    {
        if (writePacket == null)
            writePacket = WritePacketSig.GetDelegate<WritePacketDelegate>();
        
        fixed (ContentsReplayModule* ptr = &this)
        fixed (byte* dataPtr = data)
            return writePacket.Invoke(ptr, objectID, opcode, dataPtr, (ulong)data.Length);
    }

    public static readonly CompSig ReplayPacketSig = new("E8 ?? ?? ?? ?? 80 BB ?? ?? ?? ?? ?? 77 9A");
    public delegate        bool    ReplayPacketDelegate(ContentsReplayModule* contentsReplayModule, FFXIVReplay.DataSegment* segment, byte* data);
    private static         ReplayPacketDelegate? replayPacket;
    
    public bool ReplayPacket(FFXIVReplay.DataSegment* segment)
    {
        if (replayPacket == null)
            replayPacket = ReplayPacketSig.GetDelegate<ReplayPacketDelegate>();
        
        fixed (ContentsReplayModule* ptr = &this)
            return replayPacket.Invoke(ptr, segment, segment->Data);
    }

    public bool ReplayPacket(FFXIVReplay.DataSegment segment, byte[] data)
    {
        if (replayPacket == null)
            replayPacket = ReplayPacketSig.GetDelegate<ReplayPacketDelegate>();
        
        fixed (ContentsReplayModule* ptr = &this)
        fixed (byte* dataPtr = data)
            return replayPacket.Invoke(ptr, &segment, dataPtr);
    }
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct FFXIVReplay
{
    public const short CurrentReplayFormatVersion = 5;

    [StructLayout(LayoutKind.Explicit, Size = 0x68)]
    public struct Header
    {
        private static readonly byte[] validBytes = "FFXIVREPLAY"u8.ToArray();

        [FieldOffset(0x0)]  public fixed byte   FFXIVREPLAY[12];
        [FieldOffset(0xC)]  public       short  replayFormatVersion;
        [FieldOffset(0xE)]  public       short  operatingSystemType;
        [FieldOffset(0x10)] public       int    gameBuildNumber;
        [FieldOffset(0x14)] public       uint   timestamp;
        [FieldOffset(0x18)] public       uint   totalMS;
        [FieldOffset(0x1C)] public       uint   displayedMS;
        [FieldOffset(0x20)] public       ushort contentID;
        [FieldOffset(0x28)] public       byte   info;
        [FieldOffset(0x30)] public       ulong  localCID;
        [FieldOffset(0x38)] public fixed byte   jobs[8];
        [FieldOffset(0x40)] public       byte   playerIndex;
        [FieldOffset(0x44)] public       int    u0x44;
        [FieldOffset(0x48)] public       int    replayLength;
        [FieldOffset(0x4C)] public       short  u0x4C;
        [FieldOffset(0x4E)] public fixed ushort npcNames[7];
        [FieldOffset(0x5C)] public       int    u0x5C;
        [FieldOffset(0x60)] public       long   u0x60;

        public bool IsValid
        {
            get
            {
                for (var i = 0; i < validBytes.Length; i++)
                {
                    if (validBytes[i] != FFXIVREPLAY[i])
                        return false;
                }
                return true;
            }
        }

        public bool IsPlayable => gameBuildNumber == ContentsReplayModule.Instance()->gameBuildNumber && IsCurrentFormatVersion;

        public bool IsCurrentFormatVersion => replayFormatVersion == CurrentReplayFormatVersion;

        public bool IsLocked => IsValid && IsPlayable && (info & 2) != 0;

        public ContentFinderCondition ContentFinderCondition => 
            (ContentFinderCondition)Service.Data.GetExcelSheet<ContentFinderCondition>().GetRowOrDefault(contentID)!;

        public ClassJob LocalPlayerClassJob => 
            (ClassJob)Service.Data.GetExcelSheet<ClassJob>().GetRowOrDefault(jobs[playerIndex])!;

        private byte GetJobSafe(int i) => jobs[i];

        public IEnumerable<ClassJob> ClassJobs => Enumerable.Range(0, 8)
                                                            .Select(GetJobSafe).TakeWhile(id => id != 0)
                                                            .Select(id => Service.Data.GetExcelSheet<ClassJob>().GetRowOrDefault(id))
                                                            .OfType<ClassJob>();
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x4 + (0xC * 64))]
    public struct ChapterArray
    {
        [FieldOffset(0x0)] public int length;

        [StructLayout(LayoutKind.Sequential, Size = 0xC)]
        public struct Chapter
        {
            public int  type;
            public uint offset;
            public uint ms;
        }

        public Chapter* this[int i]
        {
            get
            {
                if (i is < 0 or > 63)
                    return null;

                fixed (void* ptr = &this)
                    return (Chapter*)((nint)ptr + 4) + i;
            }
        }
        
        public byte FindPreviousChapterFromTime(uint ms)
        {
            for (var i = (byte)(length - 1); i > 0; i--)
                if (this[i]->ms <= ms) return i;
            
            return 0;
        }

        public byte FindPreviousChapterType(byte chapter, byte type)
        {
            for (var i = chapter; i > 0; i--)
                if (this[i]->type == type) return i;
            
            return 0;
        }
        
        public static byte FindPreviousChapterType(byte type) =>
            ContentsReplayModule.Instance()->chapters.FindPreviousChapterType(ContentsReplayModule.GetCurrentChapter(), type);
        
        public byte FindNextChapterType(byte chapter, byte type)
        {
            for (var i = ++chapter; i < length; i++)
                if (this[i]->type == type) return i;
            
            return 0;
        }
        
        public static byte FindNextChapterType(byte type) => 
            ContentsReplayModule.Instance()->chapters.FindNextChapterType(ContentsReplayModule.GetCurrentChapter(), type);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DataSegment
    {
        public ushort opcode;
        public ushort dataLength;
        public uint ms;
        public uint objectID;

        public uint Length => (uint)sizeof(DataSegment) + dataLength;

        public byte* Data
        {
            get
            {
                fixed (void* ptr = &this)
                    return (byte*)ptr + sizeof(DataSegment);
            }
        }
    }

    public Header header;
    public ChapterArray chapters;

    public byte* Data
    {
        get
        {
            fixed (void* ptr = &this)
                return (byte*)ptr + sizeof(Header) + sizeof(ChapterArray);
        }
    }

    public DataSegment* GetDataSegment(uint offset) => offset < header.replayLength ? (DataSegment*)(Data + offset) : null;

    public DataSegment* FindNextDataSegment(uint ms, out uint offset)
    {
        offset = 0;

        DataSegment* segment;
        while ((segment = GetDataSegment(offset)) != null)
        {
            if (segment->ms >= ms) return segment;
            offset += segment->Length;
        }

        return null;
    }
    
    public (int pulls, TimeSpan longestPull) GetPullInfo()
    {
        var pulls       = 0;
        var longestPull = TimeSpan.Zero;
        for (byte j = 0; j < chapters.length; j++)
        {
            var chapter = chapters[j];
            if (chapter->type != 2 && j != 0) continue;

            if (j < chapters.length - 1)
            {
                var nextChapter = chapters[j + 1];
                if (nextChapter->type == 1)
                {
                    chapter = nextChapter;
                    j++;
                }
            }

            var nextStartMS = chapters.FindNextChapterType(j, 2) is var nextStart && nextStart > 0 ? chapters[nextStart]->ms : header.totalMS;
            var ms          = (int)(nextStartMS - chapter->ms);
            if (ms > 30_000) pulls++;

            var timeSpan = new TimeSpan(0, 0, 0, 0, ms);
            if (timeSpan > longestPull)
                longestPull = timeSpan;
        }
        return (pulls, longestPull);
    }
}
