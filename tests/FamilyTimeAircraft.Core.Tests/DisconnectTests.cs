using System.Numerics;
using Xunit;

namespace FamilyTimeAircraft.Core.Tests;

public sealed class DisconnectTests
{
    [Fact]
    public void SavedRemotePlayerIdsCannotBecomePilotsInANewSession()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", new Vector3(0, 20, 120), Quaternion.Identity);
        world.BuildAirfield("family", Vector3.Zero, Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Player, 3, "family"), 0);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, 4, "family"));
        world.SetManualControl(plane.Id, 3, new FlightInput(0.7f, 0, 0, 0, false));
        world.ReleaseRemotePlayers(0, new FlatEnvironment());
        Assert.False(plane.Seats.ContainsKey(0));
        Assert.Single(plane.Seats);
        Assert.Equal(FlightPhase.Cruising, plane.Phase);
        Assert.Throws<System.InvalidOperationException>(() => world.SetManualControl(plane.Id, 3, default));
    }

    [Fact]
    public void DisconnectedPilotReturnsRemainingPassengersToAirfield()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", new Vector3(0, 20, 120), Quaternion.Identity);
        Airfield field = world.BuildAirfield("family", Vector3.Zero, Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Player, 3, "family"), 0);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, 4, "family"));
        world.SetManualControl(plane.Id, 3, new FlightInput(0.7f, 0, 0, 0, false));
        world.PlayerDisconnected(3, new FlatEnvironment());
        Assert.False(plane.Seats.ContainsKey(0));
        for (int i = 0; i < 2000 && plane.Phase != FlightPhase.Parked; i++) world.Tick(0.05f, new FlatEnvironment());
        Assert.Equal(FlightPhase.Parked, plane.Phase);
        Assert.InRange(Vector3.Distance(field.Position, plane.Position), 0, 4);
        Assert.Single(plane.Seats);
    }

    [Fact]
    public void DisconnectedPilotWithoutAirfieldMakesEmergencyLanding()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", new Vector3(0, 20, 0), Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Player, 3, "family"), 0);
        world.SetManualControl(plane.Id, 3, new FlightInput(0.7f, 0, 0, 0, false));
        world.PlayerDisconnected(3, new FlatEnvironment());
        for (int i = 0; i < 2000 && plane.Phase != FlightPhase.Parked; i++) world.Tick(0.05f, new FlatEnvironment());
        Assert.Equal(FlightPhase.Parked, plane.Phase);
        Assert.Equal(0, plane.Position.Y);
        Assert.InRange(Vector3.Distance(Vector3.Transform(Vector3.UnitY, plane.Rotation), Vector3.UnitY), 0, 0.001f);
        Assert.Empty(world.DrainEvents());
    }
}
