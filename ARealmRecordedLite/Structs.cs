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
    
    public static void SetSavedReplayCIDs()
    {
        if (Instance()->SavedReplayHeaders == null) return;

        // 始终为 0 避免信息泄露
        const ulong contentID = 0;

        for (var i = 0; i < 3; i++)
        {
            var header = Instance()->SavedReplayHeaders[i];
            if (!header.IsValid) continue;
            
            header.LocalCID                   = contentID;
            Instance()->SavedReplayHeaders[i] = header;
        }
    }
    
    public static byte GetCurrentChapter() => Instance()->ReplayChapters.FindPreviousChapterFromTime((uint)(Instance()->Seek * 1000));


    [StructLayout(LayoutKind.Explicit, Size = 0x70)]
    public struct InitZonePacket
    {
        [FieldOffset(0x0)] public ushort Unknown0x0;
        [FieldOffset(0x2)] public ushort TerritoryType;
        [FieldOffset(0x4)] public ushort Unknown0x4;
        [FieldOffset(0x6)] public ushort ContentFinderCondition;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0xC0)]
    public struct UnknownPacket;

    [FieldOffset(0x0)]   public int                      GameBuildNumber;
    [FieldOffset(0x8)]   public nint                     FileStream;
    [FieldOffset(0x10)]  public nint                     FileStreamNextWrite;
    [FieldOffset(0x18)]  public nint                     FileStreamEnd;
    [FieldOffset(0x20)]  public long                     Unknown0x20;
    [FieldOffset(0x28)]  public long                     Unknown0x28;
    [FieldOffset(0x30)]  public long                     DataOffset;
    [FieldOffset(0x38)]  public long                     OverallDataOffset;
    [FieldOffset(0x40)]  public long                     LastDataOffset;
    [FieldOffset(0x48)]  public FFXIVReplay.Header       ReplayHeader;
    [FieldOffset(0xB0)]  public FFXIVReplay.ChapterArray ReplayChapters;
    [FieldOffset(0x3B8)] public Utf8String               ContentTitle;
    [FieldOffset(0x420)] public long                     NextDataSection;
    [FieldOffset(0x428)] public long                     NumberBytesRead;
    [FieldOffset(0x430)] public int                      CurrentFileSection;
    [FieldOffset(0x434)] public int                      DataLoadType;
    [FieldOffset(0x438)] public long                     DataLoadOffset;
    [FieldOffset(0x440)] public long                     DataLoadLength;
    [FieldOffset(0x448)] public long                     dataLoadFileOffset;
    [FieldOffset(0x450)] public long                     LocalCID;
    [FieldOffset(0x458)] public byte                     CurrentReplaySlot;
    [FieldOffset(0x460)] public Utf8String               CharacterRecordingName;
    [FieldOffset(0x4C8)] public Utf8String               ReplayTitle;
    [FieldOffset(0x530)] public Utf8String               Unknown0x530;
    [FieldOffset(0x598)] public float                    RecordingTime;
    [FieldOffset(0x5A0)] public long                     RecordingLength;
    [FieldOffset(0x5A8)] public int                      Unknown0x5A8;
    [FieldOffset(0x5AC)] public byte                     Unknown0x5AC;
    [FieldOffset(0x5AD)] public byte                     NextReplaySaveSlot;
    [FieldOffset(0x5B0)] public FFXIVReplay.Header*      SavedReplayHeaders;
    [FieldOffset(0x5B8)] public nint                     Unknown0x5B8;
    [FieldOffset(0x5C0)] public nint                     Unknown0x5C0;
    [FieldOffset(0x5C8)] public byte                     Unknown0x5C8;
    [FieldOffset(0x5CC)] public uint                     LocalPlayerObjectID;
    [FieldOffset(0x5D0)] public InitZonePacket           InitZonePacketData;
    [FieldOffset(0x640)] public long                     Unknown0x640;
    [FieldOffset(0x648)] public UnknownPacket            Unknown0x648;
    [FieldOffset(0x708)] public int                      Unknown0x708;
    [FieldOffset(0x70C)] public float                    Seek;
    [FieldOffset(0x710)] public float                    SeekDelta;
    [FieldOffset(0x714)] public float                    Speed;
    [FieldOffset(0x718)] public float                    Unknown0x718;
    [FieldOffset(0x71C)] public byte                     SelectedChapter;
    [FieldOffset(0x720)] public uint                     StartingMS;
    [FieldOffset(0x724)] public int                      Unknown0x724;
    [FieldOffset(0x728)] public short                    Unknown0x728;
    [FieldOffset(0x72A)] public byte                     Status;
    [FieldOffset(0x72B)] public byte                     PlaybackControls;
    [FieldOffset(0x72C)] public byte                     Unknown0x72C;

    public bool InPlayback       => (PlaybackControls & 4)    != 0;
    public bool IsPaused         => (PlaybackControls & 8)    != 0;
    public bool IsSavingPackets  => (Status           & 4)    != 0;
    public bool IsRecording      => (Status           & 0x74) == 0x74;
    public bool IsLoadingChapter => SelectedChapter           < 0x40;
    
    public void FixNextReplaySaveSlot(int maxAutoRenamedReplays)
    {
        if (maxAutoRenamedReplays <= 0 && !SavedReplayHeaders[NextReplaySaveSlot].IsLocked) return;

        for (byte i = 0; i < 3; i++)
        {
            if (i != 2)
            {
                var header = SavedReplayHeaders[i];
                if (header.IsLocked) continue;
            }

            NextReplaySaveSlot = i;
            return;
        }
    }
    

    public static readonly CompSig                 BeginRecordingSig = new("E8 ?? ?? ?? ?? 48 8B 5C 24 ?? 48 83 C4 20 41 5C");
    public delegate        void                    BeginRecordingDelegate(ContentsReplayModule* module, bool saveRecording);
    private static         BeginRecordingDelegate? beginRecording;
    
    public void BeginRecording(bool saveRecording = true)
    {
        beginRecording ??= BeginRecordingSig.GetDelegate<BeginRecordingDelegate>();

        fixed (ContentsReplayModule* ptr = &this)
            beginRecording.Invoke(ptr, saveRecording);
    }
    

    public static readonly CompSig               EndRecordingSig = new("E8 ?? ?? ?? ?? 32 C0 EB B2");
    public delegate        void                  EndRecordingDelegate(ContentsReplayModule* contentsReplayModule);
    private static         EndRecordingDelegate? endRecording;
    
    public void EndRecording()
    {
        endRecording ??= EndRecordingSig.GetDelegate<EndRecordingDelegate>();
        fixed (ContentsReplayModule* ptr = &this)
            endRecording.Invoke(ptr);
    }


    public static readonly CompSig                 OnZoneInPacketSig = new("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 49 8B F8 8B F2 48 8B D9 E8 ?? ?? ?? ?? F6 83");
    public delegate        void                    OnZoneInPacketDelegate(ContentsReplayModule* contentsReplayModule, uint objectID, nint packet);
    private static         OnZoneInPacketDelegate? onZoneInPacket;

    public void OnZoneInPacket(uint objectID, nint packet)
    {
        onZoneInPacket ??= OnZoneInPacketSig.GetDelegate<OnZoneInPacketDelegate>();

        fixed(ContentsReplayModule* ptr = &this)
            onZoneInPacket.Invoke(ptr, objectID, packet);
    }


    public static readonly CompSig                      InitializeRecordingSig = new("40 55 57 48 8D 6C 24 ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 27 F6 81");
    public delegate        void                         InitializeRecordingDelegate(ContentsReplayModule* contentsReplayModule);
    private static         InitializeRecordingDelegate? initializeRecording;

    public void InitializeRecording()
    {
        initializeRecording ??= InitializeRecordingSig.GetDelegate<InitializeRecordingDelegate>();

        fixed (ContentsReplayModule* ptr = &this)
            initializeRecording.Invoke(ptr);
    }


    public static readonly CompSig                  RequestPlaybackSig = new("48 89 5C 24 08 57 48 83 EC 30 F6 81 ?? ?? ?? ?? 04");
    public delegate        bool                     RequestPlaybackDelegate(ContentsReplayModule* contentsReplayModule, byte slot);
    private static         RequestPlaybackDelegate? requestPlayback;

    public bool RequestPlayback(byte slot = 0)
    {
        requestPlayback ??= RequestPlaybackSig.GetDelegate<RequestPlaybackDelegate>();

        fixed (ContentsReplayModule* ptr = &this)
            return requestPlayback.Invoke(ptr, slot);
    }

    
    public static readonly CompSig                            ReceiveActorControlPacketSig = new("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 41 56 41 57 48 83 EC 30 33 FF 48 8B D9");
    public delegate        void                               ReceiveActorControlPacketDelegate(ContentsReplayModule* contentsReplayModule, uint objectID, nint packet);
    private static         ReceiveActorControlPacketDelegate? receiveActorControlPacket;

    public void ReceiveActorControlPacket(uint objectID, nint packet)
    {
        receiveActorControlPacket ??= ReceiveActorControlPacketSig.GetDelegate<ReceiveActorControlPacketDelegate>();

        fixed (ContentsReplayModule* ptr = &this)
            receiveActorControlPacket.Invoke(ptr, objectID, packet);
    }


    public static readonly CompSig                BeginPlaybackSig = new("40 53 48 83 EC 30 0F B6 81 ?? ?? ?? ?? 48 8B D9 A8 01 0F 84 ?? ?? ?? ?? 24 FE");
    public delegate        void                   BeginPlaybackDelegate(ContentsReplayModule* contentsReplayModule, bool allowed);
    private static         BeginPlaybackDelegate? beginPlayback;
    
    public void BeginPlayback(bool allowed = true)
    {
        beginPlayback ??= BeginPlaybackSig.GetDelegate<BeginPlaybackDelegate>();

        fixed (ContentsReplayModule* ptr = &this)
            beginPlayback.Invoke(ptr, allowed);
    }
    
    
    public static readonly CompSig             SetChapterSig = new("E8 ?? ?? ?? ?? 84 C0 E9 ?? ?? ?? ?? 48 8D 4F 10");
    public delegate        bool                SetChapterDelegate(ContentsReplayModule* contentsReplayModule, byte chapter);
    private static         SetChapterDelegate? setChapter;

    public bool SetChapter(byte chapter)
    {
        setChapter ??= SetChapterSig.GetDelegate<SetChapterDelegate>();

        fixed (ContentsReplayModule* ptr = &this)
            return setChapter.Invoke(ptr, chapter);
    }

    public static readonly CompSig WritePacketSig = new("E8 ?? ?? ?? ?? 84 C0 74 60 33 C0");
    public delegate        bool    WritePacketDelegate(ContentsReplayModule* contentsReplayModule, uint objectID, ushort opcode, byte* data, ulong length);
    private static         WritePacketDelegate? writePacket;
    
    public bool WritePacket(uint objectID, ushort opcode, byte* data, ulong length)
    {
        writePacket ??= WritePacketSig.GetDelegate<WritePacketDelegate>();

        fixed (ContentsReplayModule* ptr = &this)
            return writePacket.Invoke(ptr, objectID, opcode, data, length);
    }

    public bool WritePacket(uint objectID, ushort opcode, byte[] data)
    {
        writePacket ??= WritePacketSig.GetDelegate<WritePacketDelegate>();

        fixed (ContentsReplayModule* ptr = &this)
        fixed (byte* dataPtr = data)
            return writePacket.Invoke(ptr, objectID, opcode, dataPtr, (ulong)data.Length);
    }

    public static readonly CompSig ReplayPacketSig = new("E8 ?? ?? ?? ?? 80 BB ?? ?? ?? ?? ?? 77 9A");
    public delegate        bool    ReplayPacketDelegate(ContentsReplayModule* contentsReplayModule, FFXIVReplay.DataSegment* segment, byte* data);
    private static         ReplayPacketDelegate? replayPacket;
    
    public bool ReplayPacket(FFXIVReplay.DataSegment* segment)
    {
        replayPacket ??= ReplayPacketSig.GetDelegate<ReplayPacketDelegate>();

        fixed (ContentsReplayModule* ptr = &this)
            return replayPacket.Invoke(ptr, segment, segment->Data);
    }

    public bool ReplayPacket(FFXIVReplay.DataSegment segment, byte[] data)
    {
        replayPacket ??= ReplayPacketSig.GetDelegate<ReplayPacketDelegate>();

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
        [FieldOffset(0x0)]  public fixed byte   FFXIVREPLAY[12];
        [FieldOffset(0xC)]  public       short  ReplayFormatVersion;
        [FieldOffset(0xE)]  public       short  OperatingSystemType;
        [FieldOffset(0x10)] public       int    GameBuildNumber;
        [FieldOffset(0x14)] public       uint   Timestamp;
        [FieldOffset(0x18)] public       uint   TotalMS;
        [FieldOffset(0x1C)] public       uint   DisplayedMS;
        [FieldOffset(0x20)] public       ushort ContentFinderConditionID;
        [FieldOffset(0x28)] public       byte   Info;
        [FieldOffset(0x30)] public       ulong  LocalCID;
        [FieldOffset(0x38)] public fixed byte   Jobs[8];
        [FieldOffset(0x40)] public       byte   PlayerIndex;
        [FieldOffset(0x44)] public       int    Unknown0x44;
        [FieldOffset(0x48)] public       int    ReplayLength;
        [FieldOffset(0x4C)] public       short  Unknown0x4C;
        [FieldOffset(0x4E)] public fixed ushort NPCNames[7];
        [FieldOffset(0x5C)] public       int    Unknown0x5C;
        [FieldOffset(0x60)] public       long   Unknown0x60;
        
        private static readonly byte[] validBytes = "FFXIVREPLAY"u8.ToArray();

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

        public bool IsPlayable => GameBuildNumber == ContentsReplayModule.Instance()->GameBuildNumber && IsCurrentFormatVersion;

        public bool IsCurrentFormatVersion => ReplayFormatVersion == CurrentReplayFormatVersion;

        public bool IsLocked => IsValid && IsPlayable && (Info & 2) != 0;

        public ContentFinderCondition ContentFinderCondition => 
            (ContentFinderCondition)Service.Data.GetExcelSheet<ContentFinderCondition>().GetRowOrDefault(ContentFinderConditionID)!;

        public ClassJob LocalPlayerClassJob => 
            (ClassJob)Service.Data.GetExcelSheet<ClassJob>().GetRowOrDefault(Jobs[PlayerIndex])!;

        private byte GetJobSafe(int i) => Jobs[i];

        public IEnumerable<ClassJob> ClassJobs => Enumerable.Range(0, 8)
                                                            .Select(GetJobSafe).TakeWhile(id => id != 0)
                                                            .Select(id => Service.Data.GetExcelSheet<ClassJob>().GetRowOrDefault(id))
                                                            .OfType<ClassJob>();
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x4 + (0xC * 64))]
    public struct ChapterArray
    {
        [FieldOffset(0x0)] public int Length;

        [StructLayout(LayoutKind.Sequential, Size = 0xC)]
        public struct Chapter
        {
            public int  Type;
            public uint Offset;
            public uint MS;
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
            for (var i = (byte)(Length - 1); i > 0; i--)
                if (this[i]->MS <= ms) return i;
            
            return 0;
        }

        public byte FindPreviousChapterType(byte chapter, byte type)
        {
            for (var i = chapter; i > 0; i--)
                if (this[i]->Type == type) return i;
            
            return 0;
        }
        
        public static byte FindPreviousChapterType(byte type) =>
            ContentsReplayModule.Instance()->ReplayChapters.FindPreviousChapterType(ContentsReplayModule.GetCurrentChapter(), type);
        
        public byte FindNextChapterType(byte chapter, byte type)
        {
            for (var i = ++chapter; i < Length; i++)
                if (this[i]->Type == type) return i;
            
            return 0;
        }
        
        public static byte FindNextChapterType(byte type) => 
            ContentsReplayModule.Instance()->ReplayChapters.FindNextChapterType(ContentsReplayModule.GetCurrentChapter(), type);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DataSegment
    {
        public ushort Opcode;
        public ushort DataLength;
        public uint   MS;
        public uint   ObjectID;

        public uint Length => (uint)sizeof(DataSegment) + DataLength;

        public byte* Data
        {
            get
            {
                fixed (void* ptr = &this)
                    return (byte*)ptr + sizeof(DataSegment);
            }
        }
    }

    public Header       ReplayHeader;
    public ChapterArray ReplayChapters;

    public byte* Data
    {
        get
        {
            fixed (void* ptr = &this)
                return (byte*)ptr + sizeof(Header) + sizeof(ChapterArray);
        }
    }

    public DataSegment* GetDataSegment(uint offset) => offset < ReplayHeader.ReplayLength ? (DataSegment*)(Data + offset) : null;

    public DataSegment* FindNextDataSegment(uint ms, out uint offset)
    {
        offset = 0;

        DataSegment* segment;
        while ((segment = GetDataSegment(offset)) != null)
        {
            if (segment->MS >= ms) return segment;
            offset += segment->Length;
        }

        return null;
    }
    
    public (int Pulls, TimeSpan LongestPulls) GetPullInfo()
    {
        var pulls       = 0;
        var longestPull = TimeSpan.Zero;
        for (byte j = 0; j < ReplayChapters.Length; j++)
        {
            var chapter = ReplayChapters[j];
            if (chapter->Type != 2 && j != 0) continue;

            if (j < ReplayChapters.Length - 1)
            {
                var nextChapter = ReplayChapters[j + 1];
                if (nextChapter->Type == 1)
                {
                    chapter = nextChapter;
                    j++;
                }
            }

            var nextStartMS = ReplayChapters.FindNextChapterType(j, 2) is var nextStart && nextStart > 0 ? ReplayChapters[nextStart]->MS : ReplayHeader.TotalMS;
            var ms          = (int)(nextStartMS - chapter->MS);
            if (ms > 30_000) pulls++;

            var timeSpan = new TimeSpan(0, 0, 0, 0, ms);
            if (timeSpan > longestPull)
                longestPull = timeSpan;
        }
        return (pulls, longestPull);
    }
}
