using System;
using System.Collections.Generic;
using Dalamud.Hooking;
using Dalamud.Memory;
using CompSig = ARealmRecordedLite.Utilities.CompSig;

namespace ARealmRecordedLite.Managers;

public static unsafe class ReplayPacketManager
{
    public static Dictionary<uint, CustomReplayPacket> CustomPackets { get; set; } = [];

    private static readonly List<Type>                   customPacketTypes = [typeof(RSVPacket), typeof(RSFPacket)];
    private static readonly List<(uint, ushort, byte[])> buffer            = [];

    public static void Init()
    {
        foreach (var t in customPacketTypes)
        {
            try
            {
                var packet = Activator.CreateInstance(t) as CustomReplayPacket;
                if (packet == null) continue;

                CustomPackets.Add(packet.Opcode, packet);
            }
            catch (Exception e)
            {
                Service.Log.Error(e, $"初始化自定义包处理模块 {t} 时失败");
            }
        }
    }

    public static void Uninit()
    {
        foreach (var packet in CustomPackets)
            packet.Value.Dispose();
    }

    public static bool ReplayPacket(FFXIVReplay.DataSegment* segment, byte* data)
    {
        if (!CustomPackets.TryGetValue(segment->opcode, out var packet)) return false;

        packet.Replay(segment, data);
        return true;
    }

    public static void WriteBuffer(uint objectID, ushort opcode, byte[] data)
    {
        buffer.Add((objectID, opcode, data));
        if (buffer.Count == 1)
            Service.Framework.RunOnTick(buffer.Clear, new TimeSpan(0, 0, 10));
    }

    public static void FlushBuffer()
    {
        if (ContentsReplayModule.Instance()->IsSavingPackets && buffer.Count > 0)
        {
            foreach (var (objectID, opcode, data) in buffer)
                ContentsReplayModule.Instance()->WritePacket(objectID, opcode, data);
        }

        buffer.Clear();
    }

    public abstract class CustomReplayPacket : IDisposable
    {
        public abstract ushort Opcode { get; }

        protected void Write(uint objectID, byte[] data)
        {
            if (ContentsReplayModule.Instance()->IsRecording)
                ContentsReplayModule.Instance()->WritePacket(objectID, Opcode, data);
            else
                WriteBuffer(objectID, Opcode, data);
        }

        public abstract void Replay(FFXIVReplay.DataSegment* segment, byte* data);

        public abstract void Dispose();
    }

    public class RSVPacket : CustomReplayPacket
    {
        public override ushort Opcode => 0xF001;

        private static readonly CompSig RsvReceiveSig = new("44 8B 09 4C 8D 41 34");

        private delegate bool RsvReceiveDelegate(byte* data);

        private Hook<RsvReceiveDelegate>? RsvReceiveHook;

        public RSVPacket()
        {
            RsvReceiveHook ??= RsvReceiveSig.GetHook<RsvReceiveDelegate>(RsvReceiveDetour);
            RsvReceiveHook.Enable();
        }

        private bool RsvReceiveDetour(byte* data)
        {
            var size   = *(int*)data;
            var length = size + 0x4 + 0x30;
            Write(0xE000_0000, MemoryHelper.ReadRaw((nint)data, length));
            return RsvReceiveHook.Original(data);
        }

        public override void Replay(FFXIVReplay.DataSegment* segment, byte* data) => RsvReceiveHook.Original(data);

        public override void Dispose()
        {
            RsvReceiveHook?.Dispose();
            RsvReceiveHook = null;
        }
    }

    public class RSFPacket : CustomReplayPacket
    {
        public override ushort Opcode => 0xF002;

        private static readonly CompSig RsfReceiveSig = new("48 8B 11 4C 8D 41 08");

        private delegate bool RsfReceiveDelegate(byte* data);

        private Hook<RsfReceiveDelegate>? RsfReceiveHook;

        public RSFPacket()
        {
            RsfReceiveHook ??= RsfReceiveSig.GetHook<RsfReceiveDelegate>(RsfReceiveDetour);
            RsfReceiveHook.Enable();
        }

        private bool RsfReceiveDetour(byte* data)
        {
            Write(0xE000_0000, MemoryHelper.ReadRaw((nint)data, 0x48));
            return RsfReceiveHook.Original(data);
        }

        public override void Replay(FFXIVReplay.DataSegment* segment, byte* data) => RsfReceiveHook.Original(data);

        public override void Dispose()
        {
            RsfReceiveHook?.Dispose();
            RsfReceiveHook = null;
        }
    }
}

