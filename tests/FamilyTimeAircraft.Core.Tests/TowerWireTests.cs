using System.IO;
using System.Numerics;
using Xunit;

namespace FamilyTimeAircraft.Core.Tests;

public sealed class TowerWireTests
{
    [Fact]
    public void TowerFailureEventRoundTripsWithTypedFields()
    {
        var world = new AircraftWorld();
        TowerPair pair = world.BuildTowerPair("owner", Vector3.Zero, Quaternion.Identity);
        TowerEvent expected = world.ApplyTowerImpact(pair.Id, 4, 36, Vector3.UnitZ, -Vector3.UnitZ, 7)[0];

        TowerEvent actual = AircraftWire.DecodeTowerEvent(AircraftWire.EncodeTowerEvent(expected));

        Assert.Equal(expected.Sequence, actual.Sequence);
        Assert.Equal(expected.TowerPairId, actual.TowerPairId);
        Assert.Equal(expected.TowerIndex, actual.TowerIndex);
        Assert.Equal(expected.SectionId, actual.SectionId);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Integrity, actual.Integrity);
    }

    [Fact]
    public void TowerEventRejectsTruncationAndTrailingBytes()
    {
        var world = new AircraftWorld();
        TowerPair pair = world.BuildTowerPair("owner", Vector3.Zero, Quaternion.Identity);
        byte[] packet = AircraftWire.EncodeTowerEvent(world.ApplyTowerImpact(pair.Id, 1, 12, Vector3.UnitZ, -Vector3.UnitZ, 1)[0]);
        Assert.Throws<InvalidDataException>(() => AircraftWire.DecodeTowerEvent(new byte[packet.Length - 1]));
        byte[] trailing = new byte[packet.Length + 1];
        packet.CopyTo(trailing, 0);
        Assert.Throws<InvalidDataException>(() => AircraftWire.DecodeTowerEvent(trailing));
    }

    [Fact]
    public void TowerEventRejectsSectionOutsidePair()
    {
        var world = new AircraftWorld();
        TowerPair pair = world.BuildTowerPair("owner", Vector3.Zero, Quaternion.Identity);
        byte[] packet = AircraftWire.EncodeTowerEvent(world.ApplyTowerImpact(pair.Id, 1, 12, Vector3.UnitZ, -Vector3.UnitZ, 1)[0]);
        packet[14] = TowerLayout.SectionsPerPair;

        Assert.Throws<InvalidDataException>(() => AircraftWire.DecodeTowerEvent(packet));
    }

    [Fact]
    public void SnapshotRoundTripRestoresTowerStateAndEventSequence()
    {
        var world = new AircraftWorld();
        TowerPair pair = world.BuildTowerPair("owner", Vector3.Zero, Quaternion.Identity);
        world.ApplyTowerImpact(pair.Id, 1, 12, Vector3.UnitZ, -Vector3.UnitZ, 1);
        byte[][] packets = AircraftWire.EncodeSnapshot(world, 2, 1);

        WorldSnapshot snapshot = Assert.IsType<WorldSnapshot>(new SnapshotReceiver().Accept(packets[0]));

        TowerPair restored = Assert.Single(snapshot.World.TowerPairs);
        Assert.Equal(pair.Sections[1].Integrity, restored.Sections[1].Integrity);
        TowerEvent nextEvent = world.ApplyTowerImpact(pair.Id, 2, 12, Vector3.UnitZ, -Vector3.UnitZ, 1)[0];
        Assert.True(snapshot.World.ApplyTowerEvent(nextEvent));
    }

    [Fact]
    public void TowerPlacementRequestCarriesOnlyGridAndQuarterTurn()
    {
        var request = new TowerPlacementRequest(-12, 25, 3);

        TowerPlacementRequest decoded = AircraftWire.DecodeTowerPlacementRequest(AircraftWire.EncodeTowerPlacementRequest(request));

        Assert.Equal(request.GridX, decoded.GridX);
        Assert.Equal(request.GridZ, decoded.GridZ);
        Assert.Equal(request.QuarterTurns, decoded.QuarterTurns);
    }

    [Fact]
    public void TowerPlacementRejectsInvalidQuarterTurns()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new TowerPlacementRequest(1, 1, 4));
    }
}
