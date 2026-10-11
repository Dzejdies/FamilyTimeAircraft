using System;
using System.IO;
using System.Numerics;
using System.Text;
using Xunit;

namespace FamilyTimeAircraft.Core.Tests;

public sealed class ParachutePersistenceTests
{
    [Fact]
    public void LegacyVersionOneParkedAircraftLoadsWithoutParachuteState()
    {
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true))
        {
            writer.Write(0x31544146); writer.Write(1); writer.Write(0L); writer.Write(2); writer.Write(1); writer.Write(0); writer.Write(1);
            writer.Write(1); writer.Write(6); writer.Write(Encoding.UTF8.GetBytes("family"));
            WriteVector(writer, Vector3.Zero); WriteRotation(writer); WriteVector(writer, Vector3.Zero);
            writer.Write((byte)FlightPhase.Parked); writer.Write(true); writer.Write(0); writer.Write(0); writer.Write(false); writer.Write(false);
            for (int index = 0; index < 4; index++) writer.Write(0f);
            writer.Write(false);
            for (int index = 0; index < 3; index++) writer.Write(0f);
            writer.Write(0); writer.Write(0);
        }
        bytes.Position = 0;
        AircraftWorld world = WorldRecord.Read(bytes);
        Assert.Equal("family", Assert.Single(world.Aircrafts).Owner);
        Assert.Null(world.FindAircraft(1).PilotGroundTarget);
        Assert.Empty(world.Ejections);
        Assert.Equal(0, world.PendingParachuteDrops);
    }

    [Theory]
    [InlineData(AircraftCommandKind.ExitPlayer)]
    [InlineData(AircraftCommandKind.ParachuteDrop)]
    public void NewCommandsPreserveTypedIntent(AircraftCommandKind kind)
    {
        var command = new AircraftCommand(kind, 1, position: new Vector3(8, 0, 20));
        AircraftCommand decoded = AircraftWire.DecodeCommand(AircraftWire.EncodeCommand(command));
        Assert.Equal(kind, decoded.Kind);
        Assert.Equal(command.Position, decoded.Position);
    }

    [Theory]
    [InlineData(AircraftCommandKind.ExitPlayer)]
    [InlineData(AircraftCommandKind.ParachuteDrop)]
    public void PlayerResolvedCommandsRejectForgedTargetIdentity(AircraftCommandKind kind)
    {
        Assert.Throws<ArgumentException>(() => new AircraftCommand(kind, 1, 99));
    }

    private static void WriteVector(BinaryWriter writer, Vector3 value) { writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z); }
    private static void WriteRotation(BinaryWriter writer) { writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(1f); }
}
