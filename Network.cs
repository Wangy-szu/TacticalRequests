using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using TacticalRequests.Core;

namespace TacticalRequests;

public sealed class RequestMessage : INetMessage
{
    public ulong Context;
    public int Round;
    public Command Command = new(Operation.Create, 0, Need.Vulnerable, 0, Reply.Take);
    public bool ShouldBroadcast => false;
    public bool ShouldBuffer => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public void Serialize(PacketWriter w)
    {
        w.WriteByte(3); w.WriteULong(Context); w.WriteInt(Round);
        w.WriteByte((byte)Command.Op); w.WriteInt(Command.RequestId); w.WriteByte((byte)Command.Need);
        w.WriteUInt(Command.Target); w.WriteByte((byte)Command.Reply);
    }
    public void Deserialize(PacketReader r)
    {
        if (r.ReadByte() != 3) throw new InvalidDataException("Unsupported tactical protocol");
        Context = r.ReadULong(); Round = r.ReadInt();
        Command = new((Operation)r.ReadByte(), r.ReadInt(), (Need)r.ReadByte(), r.ReadUInt(), (Reply)r.ReadByte());
    }
}

public sealed class SnapshotMessage : INetMessage
{
    public ulong Context;
    public int Round;
    public int Revision;
    public List<Request> Requests = new();
    public bool ShouldBroadcast => false;
    public bool ShouldBuffer => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public void Serialize(PacketWriter w)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(Requests);
        if (data.Length > 16384) throw new InvalidDataException("Tactical snapshot too large");
        w.WriteByte(3); w.WriteULong(Context); w.WriteInt(Round); w.WriteInt(Revision);
        w.WriteUShort((ushort)data.Length); w.WriteBytes(data, data.Length);
    }
    public void Deserialize(PacketReader r)
    {
        if (r.ReadByte() != 3) throw new InvalidDataException("Unsupported tactical protocol");
        Context = r.ReadULong(); Round = r.ReadInt(); Revision = r.ReadInt();
        int count = r.ReadUShort();
        if (count > 16384) throw new InvalidDataException("Tactical snapshot too large");
        byte[] data = new byte[count];
        for (int i = 0; i < count; i++) data[i] = r.ReadByte();
        var requests = JsonSerializer.Deserialize<List<Request>>(data);
        if (!SnapshotValidation.IsValid(requests))
            throw new InvalidDataException("Invalid tactical snapshot");
        Requests = requests!;
    }
}

