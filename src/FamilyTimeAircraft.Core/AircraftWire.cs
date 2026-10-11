using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace FamilyTimeAircraft.Core;

public enum AircraftPacketKind : byte { Command = 1, SnapshotChunk = 2, Event = 3, Notice = 4, TowerEvent = 5, TowerPlacementRequest = 6 }

public static class AircraftWire
{
    public const int MaximumPacketBytes = 64 * 1024;
    public const int DefaultChunkBytes = 48 * 1024;
    public const int MinimumChunkBytes = 2048;
    public const int MaximumChunks = WorldRecord.MaximumBytes / MinimumChunkBytes;

    public static byte[] EncodeCommand(AircraftCommand command) => Encode(AircraftPacketKind.Command, writer =>
    {
        writer.Write((byte)command.Kind); writer.Write(command.AircraftId); writer.Write(command.TargetId);
        WorldRecord.WriteVector(writer, command.Position); WorldRecord.WriteInput(writer, command.Input);
    });

    public static AircraftCommand DecodeCommand(byte[] packet) => Decode(packet, AircraftPacketKind.Command, reader =>
        new AircraftCommand((AircraftCommandKind)reader.ReadByte(), reader.ReadInt32(), reader.ReadInt32(), WorldRecord.ReadVector(reader), WorldRecord.ReadInput(reader)));

    public static byte[] EncodeEvent(AircraftEvent aircraftEvent) => Encode(AircraftPacketKind.Event, writer =>
    {
        writer.Write((byte)aircraftEvent.Kind); writer.Write(aircraftEvent.Sequence); writer.Write(aircraftEvent.AircraftId);
        WorldRecord.WriteVector(writer, aircraftEvent.Position); writer.Write(aircraftEvent.Occupants.Count);
        foreach (Occupant occupant in aircraftEvent.Occupants) WorldRecord.WriteOccupant(writer, occupant);
    });

    public static AircraftEvent DecodeEvent(byte[] packet) => Decode(packet, AircraftPacketKind.Event, reader =>
    {
        var kind = (AircraftEventKind)reader.ReadByte(); long sequence = reader.ReadInt64(); int aircraft = reader.ReadInt32();
        if (!Enum.IsDefined(typeof(AircraftEventKind), kind) || sequence < 1 || aircraft < 1) throw new InvalidDataException("Invalid aircraft event.");
        Vector3 position = WorldRecord.ReadVector(reader);
        var occupants = new Occupant[WorldRecord.ReadCount(reader, AircraftRules.PassengerCapacity + 1)];
        for (int i = 0; i < occupants.Length; i++) occupants[i] = WorldRecord.ReadOccupant(reader);
        return new AircraftEvent(kind, sequence, aircraft, position, Array.AsReadOnly(occupants));
    });

    public static byte[] EncodeNotice(string notice) => Encode(AircraftPacketKind.Notice, writer => WorldRecord.WriteText(writer, notice));
    public static string DecodeNotice(byte[] packet) => Decode(packet, AircraftPacketKind.Notice, WorldRecord.ReadText);

    public static byte[] EncodeTowerEvent(TowerEvent towerEvent)
    {
        ValidateTowerEvent(towerEvent);
        return Encode(AircraftPacketKind.TowerEvent, writer =>
        {
            writer.Write(towerEvent.Sequence);
            writer.Write(towerEvent.TowerPairId);
            writer.Write((byte)towerEvent.TowerIndex);
            writer.Write((byte)towerEvent.SectionId);
            writer.Write((byte)towerEvent.Kind);
            writer.Write(towerEvent.Integrity);
        });
    }

    public static TowerEvent DecodeTowerEvent(byte[] packet) => Decode(packet, AircraftPacketKind.TowerEvent, reader =>
    {
        var towerEvent = new TowerEvent(reader.ReadInt64(), reader.ReadInt32(), reader.ReadByte(), reader.ReadByte(),
            (TowerEventKind)reader.ReadByte(), reader.ReadSingle());
        ValidateTowerEvent(towerEvent);
        return towerEvent;
    });

    public static byte[] EncodeTowerPlacementRequest(TowerPlacementRequest request) => Encode(AircraftPacketKind.TowerPlacementRequest, writer =>
    {
        writer.Write(request.GridX);
        writer.Write(request.GridZ);
        writer.Write(request.QuarterTurns);
    });

    public static TowerPlacementRequest DecodeTowerPlacementRequest(byte[] packet) => Decode(packet, AircraftPacketKind.TowerPlacementRequest, reader =>
        new TowerPlacementRequest(reader.ReadInt32(), reader.ReadInt32(), reader.ReadByte()));

    public static byte[][] EncodeSnapshot(AircraftWorld world, double hostTime, long sequence, int chunkBytes = DefaultChunkBytes, byte[]? nativeState = null)
    {
        if (double.IsNaN(hostTime) || double.IsInfinity(hostTime) || hostTime < 0 || sequence < 1 || chunkBytes < MinimumChunkBytes || chunkBytes > DefaultChunkBytes) throw new ArgumentException("Invalid aircraft snapshot header.");
        byte[] bytes = SerializeSnapshot(world, nativeState ?? Array.Empty<byte>());
        using var hash = SHA256.Create(); byte[] checksum = hash.ComputeHash(bytes);
        int count = (bytes.Length + chunkBytes - 1) / chunkBytes;
        var packets = new byte[count][];
        for (int i = 0; i < count; i++) packets[i] = EncodeChunk(bytes, checksum, hostTime, sequence, chunkBytes, i, count);
        return packets;
    }

    private static byte[] SerializeSnapshot(AircraftWorld world, byte[] nativeState)
    {
        if (nativeState.Length > MaximumPacketBytes) throw new InvalidDataException("Native construction state exceeds its limit.");
        using var core = new MemoryStream(); WorldRecord.Write(core, world);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(checked((int)core.Length)); writer.Write(core.ToArray());
        writer.Write(nativeState.Length); writer.Write(nativeState);
        if (stream.Length > WorldRecord.MaximumBytes) throw new InvalidDataException("Aircraft snapshot exceeds its limit.");
        return stream.ToArray();
    }

    public static AircraftPacketKind Kind(byte[] packet)
    {
        if (packet == null || packet.Length < 1 || packet.Length > MaximumPacketBytes || !Enum.IsDefined(typeof(AircraftPacketKind), packet[0])) throw new InvalidDataException("Invalid aircraft packet.");
        return (AircraftPacketKind)packet[0];
    }

    private static byte[] EncodeChunk(byte[] world, byte[] hash, double hostTime, long sequence, int chunkBytes, int index, int count) => Encode(AircraftPacketKind.SnapshotChunk, writer =>
    {
        writer.Write(sequence); writer.Write(hostTime); writer.Write(world.Length); writer.Write(chunkBytes);
        writer.Write(index); writer.Write(count); writer.Write(hash);
        int offset = index * chunkBytes; int length = Math.Min(chunkBytes, world.Length - offset);
        writer.Write(length); writer.Write(world, offset, length);
    });

    private static byte[] Encode(AircraftPacketKind kind, Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write((byte)kind); write(writer);
        if (stream.Length > MaximumPacketBytes) throw new InvalidDataException("Aircraft packet exceeds its limit.");
        return stream.ToArray();
    }

    private static void ValidateTowerEvent(TowerEvent towerEvent)
    {
        if (towerEvent == null || towerEvent.Sequence < 1 || towerEvent.TowerPairId < 1 ||
            towerEvent.TowerIndex < 0 || towerEvent.TowerIndex >= TowerLayout.TowerCount ||
            towerEvent.SectionId < 0 || towerEvent.SectionId >= TowerLayout.SectionsPerPair ||
            towerEvent.SectionId / TowerLayout.SectionsPerTower != towerEvent.TowerIndex ||
            !Enum.IsDefined(typeof(TowerEventKind), towerEvent.Kind) || !AircraftRules.IsFinite(towerEvent.Integrity) ||
            towerEvent.Integrity < 0 || towerEvent.Integrity > TowerRules.InitialIntegrity ||
            towerEvent.Kind == TowerEventKind.SectionFailed && towerEvent.Integrity != 0 ||
            towerEvent.Kind == TowerEventKind.SectionDamaged && towerEvent.Integrity <= 0)
            throw new InvalidDataException("Invalid tower event.");
    }

    internal static T Decode<T>(byte[] packet, AircraftPacketKind expected, Func<BinaryReader, T> read)
    {
        if (Kind(packet) != expected) throw new InvalidDataException("Unexpected aircraft packet type.");
        using var stream = new MemoryStream(packet, false); using var reader = new BinaryReader(stream, Encoding.UTF8, true); reader.ReadByte();
        try
        {
            T result = read(reader);
            if (stream.Position != stream.Length) throw new InvalidDataException("Aircraft packet has trailing bytes.");
            return result;
        }
        catch (ArgumentException error) { throw new InvalidDataException("Malformed aircraft payload.", error); }
        catch (EndOfStreamException error) { throw new InvalidDataException("Truncated aircraft payload.", error); }
    }
}

public sealed class WorldSnapshot
{
    internal WorldSnapshot(AircraftWorld world, double hostTime, byte[] nativeState) { World = world; HostTime = hostTime; NativeState = nativeState; }
    public AircraftWorld World { get; }
    public double HostTime { get; }
    public byte[] NativeState { get; }
}

internal sealed class SnapshotChunk
{
    public long Sequence;
    public double HostTime;
    public int TotalBytes;
    public int ChunkBytes;
    public int Index;
    public int Count;
    public byte[] Hash = Array.Empty<byte>();
    public byte[] Bytes = Array.Empty<byte>();
}

public sealed class SnapshotReceiver
{
    private SnapshotChunk? _header;
    private byte[]? _worldBytes;
    private bool[]? _received;
    private int _count;
    private long _lastCompleted;

    public WorldSnapshot? Accept(byte[] packet)
    {
        SnapshotChunk chunk = AircraftWire.Decode(packet, AircraftPacketKind.SnapshotChunk, ReadChunk);
        if (chunk.Sequence <= _lastCompleted || _header != null && chunk.Sequence < _header.Sequence) return null;
        if (_header == null || chunk.Sequence > _header.Sequence) Start(chunk);
        RequireSameHeader(chunk);
        if (_received![chunk.Index]) throw new InvalidDataException("Duplicate aircraft snapshot chunk.");
        Buffer.BlockCopy(chunk.Bytes, 0, _worldBytes!, chunk.Index * chunk.ChunkBytes, chunk.Bytes.Length);
        _received[chunk.Index] = true; _count++;
        return _count == chunk.Count ? Complete() : null;
    }

    public void Reset() { _header = null; _worldBytes = null; _received = null; _count = 0; _lastCompleted = 0; }

    private void Start(SnapshotChunk header)
    {
        _header = header; _worldBytes = new byte[header.TotalBytes]; _received = new bool[header.Count]; _count = 0;
    }

    private void RequireSameHeader(SnapshotChunk chunk)
    {
        SnapshotChunk header = _header!;
        if (chunk.TotalBytes != header.TotalBytes || chunk.Count != header.Count || chunk.ChunkBytes != header.ChunkBytes || chunk.HostTime != header.HostTime || !SameBytes(header.Hash, chunk.Hash)) throw new InvalidDataException("Conflicting aircraft snapshot chunks.");
    }

    private WorldSnapshot Complete()
    {
        using var hash = SHA256.Create();
        if (!SameBytes(hash.ComputeHash(_worldBytes!), _header!.Hash)) throw new InvalidDataException("Aircraft snapshot checksum mismatch.");
        using var stream = new MemoryStream(_worldBytes!, false);
        WorldSnapshot snapshot = ReadSnapshot(stream, _header.HostTime);
        _lastCompleted = _header.Sequence; _header = null; _worldBytes = null; _received = null; _count = 0;
        return snapshot;
    }

    private static WorldSnapshot ReadSnapshot(Stream stream, double hostTime)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        int coreBytes = WorldRecord.ReadCount(reader, WorldRecord.MaximumBytes);
        byte[] core = reader.ReadBytes(coreBytes);
        if (core.Length != coreBytes) throw new InvalidDataException("Truncated aircraft snapshot.");
        using var coreStream = new MemoryStream(core, false);
        AircraftWorld world = WorldRecord.Read(coreStream);
        int nativeBytes = WorldRecord.ReadCount(reader, AircraftWire.MaximumPacketBytes);
        byte[] nativeState = reader.ReadBytes(nativeBytes);
        if (nativeState.Length != nativeBytes || stream.Position != stream.Length) throw new InvalidDataException("Invalid native construction snapshot.");
        return new WorldSnapshot(world, hostTime, nativeState);
    }

    private static SnapshotChunk ReadChunk(BinaryReader reader)
    {
        var chunk = new SnapshotChunk { Sequence = reader.ReadInt64(), HostTime = reader.ReadDouble(), TotalBytes = reader.ReadInt32(), ChunkBytes = reader.ReadInt32(), Index = reader.ReadInt32(), Count = reader.ReadInt32() };
        if (chunk.Sequence < 1 || double.IsNaN(chunk.HostTime) || double.IsInfinity(chunk.HostTime) || chunk.HostTime < 0 || chunk.TotalBytes < 1 || chunk.TotalBytes > WorldRecord.MaximumBytes || chunk.ChunkBytes < AircraftWire.MinimumChunkBytes || chunk.ChunkBytes > AircraftWire.DefaultChunkBytes || chunk.Count < 1 || chunk.Count > AircraftWire.MaximumChunks || chunk.Index < 0 || chunk.Index >= chunk.Count || chunk.Count != (chunk.TotalBytes + chunk.ChunkBytes - 1) / chunk.ChunkBytes)
            throw new InvalidDataException("Invalid aircraft snapshot chunk header.");
        chunk.Hash = reader.ReadBytes(32);
        int length = reader.ReadInt32();
        if (length != Math.Min(chunk.ChunkBytes, chunk.TotalBytes - chunk.Index * chunk.ChunkBytes)) throw new InvalidDataException("Invalid aircraft snapshot chunk length.");
        chunk.Bytes = reader.ReadBytes(length);
        if (chunk.Hash.Length != 32 || chunk.Bytes.Length != length) throw new EndOfStreamException();
        return chunk;
    }

    private static bool SameBytes(byte[] first, byte[] second)
    {
        if (first.Length != second.Length) return false;
        for (int i = 0; i < first.Length; i++) if (first[i] != second[i]) return false;
        return true;
    }
}
