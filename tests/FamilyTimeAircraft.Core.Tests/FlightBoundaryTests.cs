using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Xunit;

namespace FamilyTimeAircraft.Core.Tests;

public sealed class FlightBoundaryTests
{
    [Fact]
    public void TargetedCrashCrossesRidgeBeforeHittingRequestedLocation()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", Vector3.Zero, Quaternion.Identity);
        world.BuildAirfield("family", Vector3.Zero, Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, 2, "family"), 0);
        var target = new Vector3(0, 0, 350);
        var environment = new RidgeEnvironment();
        world.OrderCrash(plane.Id, target, "family", environment);
        for (int step = 0; step < 4000 && plane.Phase != FlightPhase.Wrecked; step++) world.Tick(0.05f, environment);
        Assert.Equal(FlightPhase.Wrecked, plane.Phase);
        Assert.InRange(Vector3.Distance(target, plane.Position), 0, 0.6f);
        Assert.Single(world.DrainEvents(), item => item.Kind == AircraftEventKind.Explosion);
    }

    [Fact]
    public void OccupiedDestinationKeepsAircraftAirborneUntilRunwayClears()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", Vector3.Zero, Quaternion.Identity);
        world.BuildAirfield("family", Vector3.Zero, Quaternion.Identity);
        Airfield destination = world.BuildAirfield("family", new Vector3(0, 0, 350), Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, 2, "family"), 0);
        var environment = new OccupiedRunwayEnvironment();
        world.OrderFlight(plane.Id, destination.Id, "family", environment);
        environment.OccupiedDestination = destination.Id;
        for (int step = 0; step < 1200; step++) world.Tick(0.05f, environment);
        Assert.Equal(FlightPhase.Cruising, plane.Phase);
        Assert.False(plane.IsOnGround);
        Assert.Equal(plane.Id, destination.ReservedBy);
        environment.OccupiedDestination = null;
        for (int step = 0; step < 1200 && plane.Phase != FlightPhase.Parked; step++) world.Tick(0.05f, environment);
        Assert.Equal(FlightPhase.Parked, plane.Phase);
        Assert.Null(destination.ReservedBy);
    }

    [Fact]
    public void AutopilotClearsRidgeBeforeDescendingInsideDestinationRunway()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", Vector3.Zero, Quaternion.Identity);
        world.BuildAirfield("family", Vector3.Zero, Quaternion.Identity);
        Airfield destination = world.BuildAirfield("family", new Vector3(0, 0, 350), Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, 2, "family"), 0);
        var environment = new RidgeEnvironment();
        world.OrderFlight(plane.Id, destination.Id, "family", environment);
        for (int step = 0; step < 4000 && plane.Phase != FlightPhase.Parked && plane.Phase != FlightPhase.Wrecked; step++)
            world.Tick(0.05f, environment);
        Assert.Equal(FlightPhase.Parked, plane.Phase);
        Assert.InRange(Vector3.Distance(destination.Position, plane.Position), 0, 0.5f);
        Assert.DoesNotContain(world.DrainEvents(), item => item.Kind == AircraftEventKind.Explosion);
    }

    [Fact]
    public void RetargetingReleasesPreviousDestinationReservation()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", Vector3.Zero, Quaternion.Identity);
        world.BuildAirfield("family", Vector3.Zero, Quaternion.Identity);
        Airfield first = world.BuildAirfield("family", new Vector3(0, 0, 350), Quaternion.Identity);
        Airfield second = world.BuildAirfield("family", new Vector3(200, 0, 350), Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, 2, "family"), 0);
        world.OrderFlight(plane.Id, first.Id, "family", new FlatEnvironment());
        for (int i = 0; i < 600 && plane.Phase != FlightPhase.Cruising; i++) world.Tick(0.05f, new FlatEnvironment());
        world.OrderFlight(plane.Id, second.Id, "family", new FlatEnvironment());
        Assert.Null(first.ReservedBy);
        Assert.Equal(plane.Id, second.ReservedBy);
    }

    [Fact]
    public void CrashOrderHitsRequestedPointAndEjectsDifferentDirections()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", Vector3.Zero, Quaternion.Identity);
        world.BuildAirfield("family", Vector3.Zero, Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, 2, "family"), 0);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, 3, "family"));
        var target = new Vector3(80, 0, 130);
        world.OrderCrash(plane.Id, target, "family", new FlatEnvironment());
        for (int i = 0; i < 600 && plane.Phase != FlightPhase.Wrecked; i++) world.Tick(0.05f, new FlatEnvironment());
        Assert.InRange(Vector3.Distance(target, plane.Position), 0, 0.6f);
        Assert.Equal(2, world.Ejections.Count);
        Assert.NotEqual(world.Ejections.First().Velocity.X, world.Ejections.Last().Velocity.X);
        Assert.Single(world.DrainEvents(), item => item.Kind == AircraftEventKind.Explosion);
    }

    [Fact]
    public void StoppedAirbornePlayerCannotDisembark()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", new Vector3(0, 20, 0), Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Player, 1, "family"), 0);
        world.SetManualControl(plane.Id, 1, new FlightInput(0, 0, 0, 0, false));
        world.Tick(0.001f, new FlatEnvironment());
        Assert.Throws<InvalidOperationException>(() => world.Disembark(plane.Id, OccupantKind.Player, 1));
        Assert.Single(plane.Seats);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(1.01f)]
    [InlineData(-0.1f)]
    public void InvalidThrottleCannotEnterSimulation(float throttle) => Assert.Throws<ArgumentException>(() => new FlightInput(throttle, 0, 0, 0, false));

    [Fact]
    public void CorruptSaveDoesNotAllocateUnboundedObjects()
    {
        using var stream = new MemoryStream();
        WorldRecord.Write(stream, new AircraftWorld());
        stream.Position = 24;
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) writer.Write(int.MaxValue);
        stream.Position = 0;
        Assert.Throws<InvalidDataException>(() => WorldRecord.Read(stream));
    }

    [Fact]
    public void TruncatedSaveFailsWithoutAdoptingPartialWorld()
    {
        var world = new AircraftWorld();
        world.BuildAircraft("family", Vector3.Zero, Quaternion.Identity);
        using var stream = new MemoryStream();
        WorldRecord.Write(stream, world);
        stream.SetLength(stream.Length - 1); stream.Position = 0;
        Assert.Throws<EndOfStreamException>(() => WorldRecord.Read(stream));
        Assert.Single(world.Aircrafts);
    }

    [Fact]
    public void DisembarkingPublishesAuthoritativeExitForOtherPlayers()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", Vector3.Zero, Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Player, 4, "family"), 0);
        world.Disembark(plane.Id, OccupantKind.Player, 4, new FlatEnvironment());
        AircraftEvent exit = Assert.Single(world.DrainEvents());
        Assert.Equal(AircraftEventKind.OccupantLanded, exit.Kind);
        Assert.Equal(new Vector3(7, 0, 0), exit.Position);
        Assert.Equal(4, Assert.Single(exit.Occupants).Id);
    }

    [Fact]
    public void ExitNearWorldBoundaryUsesSafeSide()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", new Vector3(1998, 0, 0), Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Player, 4, "family"), 0);
        world.Disembark(plane.Id, OccupantKind.Player, 4, new FlatEnvironment());
        AircraftEvent exit = Assert.Single(world.DrainEvents());
        Assert.True(new FlatEnvironment().IsInWorld(exit.Position));
        Assert.Equal(1991, exit.Position.X);
    }

    [Fact]
    public void PassengersCanBoardAfterPlayerPilotStartsIdleGroundControls()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", Vector3.Zero, Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Player, 4, "family"), 0);
        world.SetManualControl(plane.Id, 4, new FlightInput(0, 0, 0, 0, true));
        world.Tick(0.02f, new FlatEnvironment());
        int seat = world.Board(plane.Id, new Occupant(OccupantKind.Child, 7, "family"));
        Assert.Equal(1, seat);
        Assert.Equal(2, plane.Seats.Count);
    }

    [Theory]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(int.MaxValue)]
    public void ChildIdentityMustReferToANativeNonNullHandle(int childId)
    {
        Assert.Throws<ArgumentException>(() => new Occupant(OccupantKind.Child, childId, "family"));
    }
}

internal sealed class RidgeEnvironment : IFlightEnvironment
{
    public bool IsInWorld(Vector3 position) => new FlatEnvironment().IsInWorld(position);
    public float GroundHeight(Vector3 position) => position.Z > 140 && position.Z < 280 ? 100 : 0;
    public bool IsBlocked(Vector3 from, Vector3 to, float radius) => GroundHeight(to) > 0 && to.Y - radius < GroundHeight(to);
    public bool IsRunwayClear(Airfield airfield) => true;
}

internal sealed class OccupiedRunwayEnvironment : IFlightEnvironment
{
    public int? OccupiedDestination { get; set; }
    public bool IsInWorld(Vector3 position) => new FlatEnvironment().IsInWorld(position);
    public float GroundHeight(Vector3 position) => 0;
    public bool IsBlocked(Vector3 from, Vector3 to, float radius) => false;
    public bool IsRunwayClear(Airfield airfield) => airfield.Id != OccupiedDestination;
}
