using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Xunit;

namespace FamilyTimeAircraft.Core.Tests;

public sealed class ParachuteTests
{
    [Fact]
    public void PassengerDropsAreStaggeredAndLeaveForeignChildrenAndPilotSeated()
    {
        AircraftWorld world = FlyingChildren(out Aircraft plane);
        world.OrderParachuteDrop(plane.Id, LandingPoint(plane), "family", new FlatEnvironment());
        world.Tick(0.1f, new FlatEnvironment());
        Assert.Equal(3, Assert.Single(world.Ejections).Occupant.Id);
        world.Tick(0.2f, new FlatEnvironment());
        Assert.Single(world.Ejections);
        world.Tick(0.3f, new FlatEnvironment());
        Assert.Equal(2, world.Ejections.Count);
        Assert.Equal(2, plane.Seats[0].Id);
        Assert.Equal(5, plane.Seats[3].Id);
        Assert.Equal(2, world.Ejections.Select(item => item.ParachuteTarget).Distinct().Count());
    }

    [Fact]
    public void ParachutistsConvergeOnDistinctTargetsAndPilotReturnsThenWalks()
    {
        AircraftWorld world = FlyingChildren(out Aircraft plane);
        Vector3 target = LandingPoint(plane);
        world.OrderParachuteDrop(plane.Id, target, "family", new FlatEnvironment());
        for (int step = 0; step < 400; step++) world.Tick(0.1f, new FlatEnvironment());
        AircraftEvent[] events = world.DrainEvents().ToArray();
        Assert.Empty(world.Ejections);
        Assert.Equal(FlightPhase.Parked, plane.Phase);
        Assert.False(plane.Seats.ContainsKey(0));
        Assert.Equal(5, Assert.Single(plane.Seats).Value.Id);
        AircraftEvent[] landings = events.Where(item => item.Kind == AircraftEventKind.OccupantLanded && item.Occupants[0].Id != 2).ToArray();
        Assert.Equal(2, landings.Length);
        Assert.All(landings, item => Assert.InRange(Vector3.Distance(item.Position, target), 0, 5));
        Assert.Equal(3, events.Count(item => item.Kind == AircraftEventKind.GroundGoTo));
    }

    [Theory]
    [InlineData(2500)]
    [InlineData(700)]
    public void InvalidLandingTargetsDoNotChangeSeatsOrFlight(float distance)
    {
        AircraftWorld world = FlyingChildren(out Aircraft plane);
        FlightPhase phase = plane.Phase;
        Assert.Throws<InvalidOperationException>(() => world.OrderParachuteDrop(plane.Id, Vector3.UnitX * distance, "family", new FlatEnvironment()));
        Assert.Equal(4, plane.Seats.Count);
        Assert.Equal(phase, plane.Phase);
        Assert.Empty(world.Ejections);
    }

    [Fact]
    public void BlockedLandingPointFailsBeforeDroppingAnyChild()
    {
        AircraftWorld world = FlyingChildren(out Aircraft plane);
        Assert.Throws<InvalidOperationException>(() => world.OrderParachuteDrop(plane.Id, LandingPoint(plane), "family", new BlockedLandingEnvironment()));
        Assert.Equal(4, plane.Seats.Count);
        Assert.Empty(world.Ejections);
    }

    [Fact]
    public void ExistingCrashOrderHasPriorityOverPilotReturn()
    {
        AircraftWorld world = FlyingChildren(out Aircraft plane);
        world.OrderCrash(plane.Id, LandingPoint(plane), "family", new FlatEnvironment());
        world.OrderParachuteDrop(plane.Id, LandingPoint(plane), "family", new FlatEnvironment());
        world.Tick(0.6f, new FlatEnvironment());
        Assert.Equal(FlightPhase.CrashApproach, plane.Phase);
        Assert.Equal(2, plane.Seats[0].Id);
        Assert.Null(plane.TargetAirfieldId);
    }

    [Fact]
    public void GroundOrderUnloadsOnlyOwnedChildrenAndIssuesTheirGoTo()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft(AircraftRules.SharedOwner, Vector3.Zero, Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, 2, "family"), 0);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, 3, "family"), 1);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, 4, "other"), 2);
        world.OrderParachuteDrop(plane.Id, new Vector3(10, 0, 10), "family", new FlatEnvironment());
        Assert.Equal(4, Assert.Single(plane.Seats).Value.Id);
        Assert.Empty(world.Ejections);
        Assert.Equal(2, world.DrainEvents().Count(item => item.Kind == AircraftEventKind.GroundGoTo));
    }

    [Fact]
    public void PendingDropAndDeployedCanopySurviveSaveAndContinueOnce()
    {
        AircraftWorld world = FlyingChildren(out Aircraft plane);
        world.OrderParachuteDrop(plane.Id, LandingPoint(plane), "family", new FlatEnvironment());
        world.Tick(0.1f, new FlatEnvironment());
        using var bytes = new MemoryStream();
        WorldRecord.Write(bytes, world);
        bytes.Position = 0;
        AircraftWorld restored = WorldRecord.Read(bytes);
        Assert.NotNull(Assert.Single(restored.Ejections).ParachuteTarget);
        restored.Tick(0.5f, new FlatEnvironment());
        Assert.Equal(2, restored.Ejections.Count);
        Assert.Equal(2, restored.FindAircraft(plane.Id).Seats.Count);
    }

    [Fact]
    public void PilotOnlyOrderReturnsToAirfieldEvenForDistantGroundTarget()
    {
        AircraftWorld world = FlyingChildren(out Aircraft plane);
        Vector3 nearby = LandingPoint(plane);
        world.OrderParachuteDrop(plane.Id, nearby, "family", new FlatEnvironment());
        world.Tick(0.6f, new FlatEnvironment());
        world.OrderParachuteDrop(plane.Id, Vector3.UnitX * 600, "family", new FlatEnvironment());
        Assert.Equal(2, plane.Seats[0].Id);
        Assert.Equal(FlightPhase.Cruising, plane.Phase);
    }

    [Fact]
    public void PilotReturnWaitsForSafeExitWhenLandingAreaBecomesBlocked()
    {
        AircraftWorld world = FlyingChildren(out Aircraft plane);
        world.OrderParachuteDrop(plane.Id, LandingPoint(plane), "family", new FlatEnvironment());
        world.Tick(0.6f, new FlatEnvironment());
        for (int step = 0; step < 400 && !plane.CanBoard; step++) world.Tick(0.1f, new ExitBlockedEnvironment());
        Assert.True(plane.CanBoard);
        Assert.True(plane.Seats.ContainsKey(0));
        world.Tick(0.1f, new FlatEnvironment());
        Assert.False(plane.Seats.ContainsKey(0));
        Assert.Single(world.DrainEvents(), item => item.Kind == AircraftEventKind.GroundGoTo && item.Occupants[0].Id == 2);
    }

    private sealed class ExitBlockedEnvironment : IFlightEnvironment
    {
        public bool IsInWorld(Vector3 position) => true;
        public float GroundHeight(Vector3 position) => 0;
        public bool IsBlocked(Vector3 from, Vector3 to, float radius) => from.Y < 1 && to.Y >= 2;
        public bool IsRunwayClear(Airfield field) => true;
    }

    [Fact]
    public void EightPassengerOrderPreflightsLastReleaseBeforeAnyDrop()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", Vector3.UnitY * 35, Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Player, 1, "family"), 0);
        for (int child = 0; child < AircraftRules.PassengerCapacity; child++) world.Board(plane.Id, new Occupant(OccupantKind.Child, child + 2, "family"));
        world.SetManualControl(plane.Id, 1, new FlightInput(1, 0, 0, 0, false));
        world.Tick(1, new FlatEnvironment());
        Vector3 target = new(plane.Position.X, 0, plane.Position.Z - 55);
        Assert.Throws<InvalidOperationException>(() => world.OrderParachuteDrop(plane.Id, target, "family", new FlatEnvironment()));
        Assert.Equal(9, plane.Seats.Count);
        Assert.Empty(world.Ejections);
        Assert.Equal(0, world.PendingParachuteDrops);
    }

    [Fact]
    public void PendingChildZeroCanResumeFromSavedDropQueue()
    {
        AircraftWorld world = FlyingChildren(out Aircraft plane, 0);
        world.OrderParachuteDrop(plane.Id, LandingPoint(plane), "family", new FlatEnvironment());
        using var bytes = new MemoryStream();
        WorldRecord.Write(bytes, world);
        bytes.Position = 0;
        AircraftWorld restored = WorldRecord.Read(bytes);
        Assert.Equal(1, restored.PendingParachuteDrops);
        restored.Tick(0.1f, new FlatEnvironment());
        Assert.Equal(0, Assert.Single(restored.Ejections).Occupant.Id);
    }

    private static Vector3 LandingPoint(Aircraft plane) => new(plane.Position.X, 0, plane.Position.Z + 30);

    private static AircraftWorld FlyingChildren(out Aircraft plane, int firstPassenger = 3)
    {
        var world = new AircraftWorld();
        plane = world.BuildAircraft(AircraftRules.SharedOwner, Vector3.Zero, Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, 2, "family"), 0);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, firstPassenger, "family"), 1);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, 4, "family"), 2);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, 5, "other"), 3);
        world.BuildAirfield("family", Vector3.Zero, Quaternion.Identity);
        Airfield destination = world.BuildAirfield("family", Vector3.UnitZ * 320, Quaternion.Identity);
        world.OrderFlight(plane.Id, destination.Id, "family", new FlatEnvironment());
        for (int step = 0; step < 200 && plane.Phase != FlightPhase.Cruising; step++) world.Tick(0.1f, new FlatEnvironment());
        Assert.Equal(FlightPhase.Cruising, plane.Phase);
        return world;
    }

    private sealed class BlockedLandingEnvironment : IFlightEnvironment
    {
        public bool IsInWorld(Vector3 position) => true;
        public float GroundHeight(Vector3 position) => 0;
        public bool IsBlocked(Vector3 from, Vector3 to, float radius) => true;
        public bool IsRunwayClear(Airfield field) => true;
    }
}
