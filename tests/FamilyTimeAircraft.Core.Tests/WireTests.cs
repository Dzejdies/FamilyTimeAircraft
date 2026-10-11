using System;
using System.IO;
using System.Numerics;
using Xunit;

namespace FamilyTimeAircraft.Core.Tests;

public sealed class WireTests
{
    [Fact]
    public void CrashCommandKeepsExactTargetAndRejectsTrailingData()
    {
        byte[] packet = AircraftWire.EncodeCommand(new AircraftCommand(AircraftCommandKind.CrashAt, 3, position: new Vector3(7, 8, 9)));
        AircraftCommand decoded = AircraftWire.DecodeCommand(packet);
        Assert.Equal(new Vector3(7, 8, 9), decoded.Position);
        Assert.Equal(3, decoded.AircraftId);
        Array.Resize(ref packet, packet.Length + 1);
        Assert.Throws<InvalidDataException>(() => AircraftWire.DecodeCommand(packet));
    }

    [Fact]
    public void FragmentedSnapshotAdoptsOnlyCompleteWorld()
    {
        var world = new AircraftWorld();
        for (int i = 0; i < 40; i++) world.BuildAircraft(new string('f', 500), new Vector3(i, 0, 0), Quaternion.Identity);
        byte[][] packets = AircraftWire.EncodeSnapshot(world, 13.5, 1, 2048);
        var receiver = new SnapshotReceiver();
        Assert.True(packets.Length > 1);
        for (int i = 0; i < packets.Length - 1; i++) Assert.Null(receiver.Accept(packets[i]));
        WorldSnapshot snapshot = Assert.IsType<WorldSnapshot>(receiver.Accept(packets[packets.Length - 1]));
        Assert.Equal(40, snapshot.World.Aircrafts.Count);
        Assert.Equal(13.5, snapshot.HostTime);
    }

    [Fact]
    public void PreviousIncompleteSnapshotCannotReplaceNewerWorld()
    {
        var old = new AircraftWorld();
        for (int i = 0; i < 40; i++) old.BuildAircraft(new string('f', 500), new Vector3(i, 0, 0), Quaternion.Identity);
        byte[][] oldPackets = AircraftWire.EncodeSnapshot(old, 1, 1, 2048);
        var newer = new AircraftWorld();
        newer.BuildAircraft("new", Vector3.Zero, Quaternion.Identity);
        var receiver = new SnapshotReceiver();
        Assert.Null(receiver.Accept(oldPackets[0]));
        WorldSnapshot accepted = Assert.IsType<WorldSnapshot>(receiver.Accept(AircraftWire.EncodeSnapshot(newer, 2, 2)[0]));
        Assert.Equal("new", Assert.Single(accepted.World.Aircrafts).Owner);
        Assert.Null(receiver.Accept(oldPackets[1]));
    }

    [Fact]
    public void AlteredSnapshotChunkFailsIntegrityCheck()
    {
        var world = new AircraftWorld();
        world.BuildAircraft("family", Vector3.Zero, Quaternion.Identity);
        byte[] packet = AircraftWire.EncodeSnapshot(world, 1, 1)[0];
        packet[packet.Length - 1] ^= 1;
        Assert.Throws<InvalidDataException>(() => new SnapshotReceiver().Accept(packet));
    }

    [Fact]
    public void SnapshotPreservesNativeConstructionProgressAlongsideAircraft()
    {
        byte[] progress = { 1, 7, 4, 8 };
        byte[] packet = AircraftWire.EncodeSnapshot(new AircraftWorld(), 1, 1, nativeState: progress)[0];
        WorldSnapshot snapshot = Assert.IsType<WorldSnapshot>(new SnapshotReceiver().Accept(packet));
        Assert.Equal(progress, snapshot.NativeState);
    }
}
