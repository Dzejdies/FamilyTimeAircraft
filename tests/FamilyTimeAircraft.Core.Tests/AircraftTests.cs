using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Xunit;

namespace FamilyTimeAircraft.Core.Tests;

public sealed class AircraftTests
{
    [Theory]
    [InlineData("family", "family", 0)]
    [InlineData("family", "other", 1)]
    [InlineData("shared", "other", 0)]
    public void AutomaticBoardingUsesPilotOnlyWhenOwnershipAllows(string owner, string playerOwner, int expectedSeat)
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft(owner, Vector3.Zero, Quaternion.Identity);
        int seat = world.Board(plane.Id, new Occupant(OccupantKind.Player, 1, playerOwner), AircraftRules.AutomaticBoardingSeat);
        Assert.Equal(expectedSeat, seat);
    }

    [Fact]
    public void AutomaticBoardingPreservesAnExistingPilot()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", Vector3.Zero, Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, 2, "family"), 0);
        Assert.Equal(1, world.Board(plane.Id, new Occupant(OccupantKind.Player, 1, "family"), AircraftRules.AutomaticBoardingSeat));
        Assert.Equal(2, plane.Seats[0].Id);
    }

    [Fact]
    public void FullCabinDoesNotLoseExistingPassengers()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", Vector3.Zero, Quaternion.Identity);
        for (int id = 1; id <= 8; id++) world.Board(plane.Id, new Occupant(OccupantKind.Child, id, "family"));
        Assert.Throws<InvalidOperationException>(() => world.Board(plane.Id, new Occupant(OccupantKind.Child, 9, "family")));
        Assert.Equal(8, plane.Seats.Count);
    }

    [Fact]
    public void ForeignChildCannotBoardPrivateAircraft()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", Vector3.Zero, Quaternion.Identity);
        Assert.Throws<InvalidOperationException>(() => world.Board(plane.Id, new Occupant(OccupantKind.Child, 1, "other")));
        Assert.Empty(plane.Seats);
    }

    [Fact]
    public void OccupantCannotReserveTwoAircraft()
    {
        var world = new AircraftWorld();
        Aircraft first = world.BuildAircraft("family", Vector3.Zero, Quaternion.Identity);
        Aircraft second = world.BuildAircraft("family", Vector3.UnitX, Quaternion.Identity);
        var child = new Occupant(OccupantKind.Child, 4, "family");
        world.Board(first.Id, child);
        Assert.Throws<InvalidOperationException>(() => world.Board(second.Id, child));
        Assert.Empty(second.Seats);
    }

    [Fact]
    public void OnlySeatedPlayerPilotCanSupplyControls()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", Vector3.Zero, Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Player, 7, "family"), 0);
        Assert.Throws<InvalidOperationException>(() => world.SetManualControl(plane.Id, 8, new FlightInput(1, 0, 0, 0, false)));
        world.SetManualControl(plane.Id, 7, new FlightInput(1, 0, 0, 0, false));
        Assert.Equal(FlightPhase.Manual, plane.Phase);
    }

    [Fact]
    public void ApplyingBrakeCutsEngineThrust()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", new Vector3(0, 100, 0), Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Player, 7, "family"), 0);
        world.SetManualControl(plane.Id, 7, new FlightInput(1, 0, 0, 0, false));
        for (int step = 0; step < 4; step++) world.Tick(0.05f, new FlatEnvironment());
        float forwardSpeedBeforeBraking = plane.Velocity.Z;
        world.SetManualControl(plane.Id, 7, new FlightInput(1, 0, 0, 0, true));

        world.Tick(0.05f, new FlatEnvironment());

        Assert.True(plane.Velocity.Z < forwardSpeedBeforeBraking, $"Braking still allowed forward engine thrust: {plane.Velocity}");
    }

    [Fact]
    public void ExplosionEjectsCabinOnceAndLeavesRepairableWreck()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", new Vector3(0, 15, 0), Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Player, 1, "family"), 0);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, 2, "family"));
        world.SetManualControl(plane.Id, 1, new FlightInput(0, 0, 0, 0, false));
        var environment = new FlatEnvironment();
        for (int i = 0; i < 600; i++) world.Tick(0.05f, environment);
        Assert.Equal(FlightPhase.Wrecked, plane.Phase);
        Assert.Empty(plane.Seats);
        Assert.Single(world.DrainEvents(), item => item.Kind == AircraftEventKind.Explosion);
        Assert.Empty(world.Ejections);
        world.Repair(plane.Id, "family");
        Assert.Equal(FlightPhase.Parked, plane.Phase);
    }

    [Fact]
    public void AutopilotLandsAtBuiltDestination()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", Vector3.Zero, Quaternion.Identity);
        Airfield origin = world.BuildAirfield("family", Vector3.Zero, Quaternion.Identity);
        Airfield target = world.BuildAirfield("family", new Vector3(0, 0, 350), Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, 2, "family"), 0);
        world.OrderFlight(plane.Id, target.Id, "family", new FlatEnvironment());
        for (int i = 0; i < 4000 && plane.Phase != FlightPhase.Parked; i++) world.Tick(0.05f, new FlatEnvironment());
        Assert.Equal(FlightPhase.Parked, plane.Phase);
        Assert.InRange(Vector3.Distance(target.Position, plane.Position), 0, 4);
        Assert.Null(origin.ReservedBy);
        Assert.Null(target.ReservedBy);
        Assert.DoesNotContain(world.DrainEvents(), item => item.Kind == AircraftEventKind.Explosion);
    }

    [Fact]
    public void BlockedDestinationDoesNotPartiallyReserveRunways()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", Vector3.Zero, Quaternion.Identity);
        Airfield origin = world.BuildAirfield("family", Vector3.Zero, Quaternion.Identity);
        Airfield target = world.BuildAirfield("family", new Vector3(0, 0, 350), Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, 2, "family"), 0);
        Assert.Throws<InvalidOperationException>(() => world.OrderFlight(plane.Id, target.Id, "family", new FlatEnvironment(false)));
        Assert.Equal(FlightPhase.Parked, plane.Phase);
        Assert.Null(origin.ReservedBy);
        Assert.Null(target.ReservedBy);
    }

    [Fact]
    public void SaveRoundTripPreservesCabinAndFlightDestination()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", Vector3.Zero, Quaternion.Identity);
        world.BuildAirfield("family", Vector3.Zero, Quaternion.Identity);
        Airfield target = world.BuildAirfield("family", new Vector3(0, 0, 350), Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, 2, "family"), 0);
        world.OrderFlight(plane.Id, target.Id, "family", new FlatEnvironment());
        world.Tick(1, new FlatEnvironment());
        using var stream = new MemoryStream();
        WorldRecord.Write(stream, world);
        stream.Position = 0;
        AircraftWorld loaded = WorldRecord.Read(stream);
        Aircraft restored = loaded.FindAircraft(plane.Id);
        Assert.Equal(plane.Position, restored.Position);
        Assert.Equal(target.Id, restored.TargetAirfieldId);
        Assert.Equal(2, restored.Seats[0].Id);
    }
}

internal sealed class FlatEnvironment : IFlightEnvironment
{
    private readonly bool _clear;
    public FlatEnvironment(bool clear = true) => _clear = clear;
    public bool IsInWorld(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z) && Math.Abs(point.X) < 2000 && Math.Abs(point.Z) < 2000 && point.Y >= -16 && point.Y <= 512;
    public float GroundHeight(Vector3 point) => 0;
    public bool IsBlocked(Vector3 from, Vector3 to, float radius) => false;
    public bool IsRunwayClear(Airfield airfield) => _clear;
}
