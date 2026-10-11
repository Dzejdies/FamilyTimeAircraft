using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Xunit;

namespace FamilyTimeAircraft.Core.Tests;

public sealed class BailoutTests
{
    [Fact]
    public void PilotBailoutPreservesMomentumAndOtherPassengers()
    {
        AircraftWorld world = FlyingWorld(out Aircraft plane);
        Vector3 velocity = plane.Velocity;
        world.ExitPlayer(plane.Id, 1, new FlatEnvironment());
        Assert.Equal(velocity, plane.Velocity);
        Assert.Equal(FlightPhase.Unpiloted, plane.Phase);
        Assert.Equal(2, plane.Seats[1].Id);
        Assert.False(plane.Seats.ContainsKey(0));
        Assert.Equal(1, Assert.Single(world.Ejections).Occupant.Id);
        Assert.Equal(0, plane.CurrentInput.Throttle);
    }

    [Fact]
    public void AbandonedPlaneContinuesForwardThenWrecksOnTerrain()
    {
        AircraftWorld world = FlyingWorld(out Aircraft plane);
        Vector3 departure = plane.Position;
        world.ExitPlayer(plane.Id, 1, new FlatEnvironment());
        for (int second = 0; second < 20; second++) world.Tick(1, new FlatEnvironment());
        Assert.True(plane.Position.Z > departure.Z + 10);
        Assert.Equal(FlightPhase.Wrecked, plane.Phase);
        Assert.Single(world.DrainEvents(), item => item.Kind == AircraftEventKind.Explosion);
    }

    [Fact]
    public void PassengerBailoutPreservesChildPilotAndAutopilotDestination()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", Vector3.Zero, Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, 2, "family"), 0);
        world.Board(plane.Id, new Occupant(OccupantKind.Player, 1, "family"), 1);
        world.BuildAirfield("family", Vector3.Zero, Quaternion.Identity);
        Airfield destination = world.BuildAirfield("family", Vector3.UnitZ * 320, Quaternion.Identity);
        world.OrderFlight(plane.Id, destination.Id, "family", new FlatEnvironment());
        world.Tick(1, new FlatEnvironment());
        FlightPhase phase = plane.Phase;
        world.ExitPlayer(plane.Id, 1, new FlatEnvironment());
        Assert.Equal(phase, plane.Phase);
        Assert.Equal(destination.Id, plane.TargetAirfieldId);
        Assert.Equal(2, plane.Seats[0].Id);
    }

    [Fact]
    public void GroundExitLeavesChildrenSeated()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", Vector3.Zero, Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Player, 1, "family"), 0);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, 2, "family"), 1);
        world.ExitPlayer(plane.Id, 1, new FlatEnvironment());
        Assert.Equal(2, Assert.Single(plane.Seats).Value.Id);
        Assert.Empty(world.Ejections);
        Assert.Equal(1, Assert.Single(world.DrainEvents()).Occupants[0].Id);
    }

    [Fact]
    public void NonseatedPlayerCannotExitAnotherPassenger()
    {
        AircraftWorld world = FlyingWorld(out Aircraft plane);
        Assert.Throws<InvalidOperationException>(() => world.ExitPlayer(plane.Id, 9, new FlatEnvironment()));
        Assert.Equal(2, plane.Seats.Count);
        Assert.Empty(world.Ejections);
    }

    [Fact]
    public void UnpilotedFlightAndBailedPlayerRoundTripThroughSave()
    {
        AircraftWorld world = FlyingWorld(out Aircraft plane);
        world.ExitPlayer(plane.Id, 1, new FlatEnvironment());
        using var bytes = new MemoryStream();
        WorldRecord.Write(bytes, world);
        bytes.Position = 0;
        AircraftWorld restored = WorldRecord.Read(bytes);
        Assert.Equal(FlightPhase.Unpiloted, restored.FindAircraft(plane.Id).Phase);
        Assert.Equal(plane.Velocity, restored.FindAircraft(plane.Id).Velocity);
        Assert.Equal(1, Assert.Single(restored.Ejections).Occupant.Id);
    }

    [Fact]
    public void PlayerCanBailAboveBuildingAndUsesNearbyClearLanding()
    {
        AircraftWorld world = FlyingWorld(out Aircraft plane);
        world.ExitPlayer(plane.Id, 1, new BuildingBelowEnvironment(plane.Position));
        EjectedOccupant player = Assert.Single(world.Ejections);
        Assert.True(player.HasParachute);
        Assert.InRange(Vector3.Distance(new Vector3(plane.Position.X, 0, plane.Position.Z), player.ParachuteTarget!.Value), 8, 32);
    }

    private sealed class BuildingBelowEnvironment : IFlightEnvironment
    {
        private readonly Vector3 _origin;
        public BuildingBelowEnvironment(Vector3 origin) { _origin = origin; }
        public bool IsInWorld(Vector3 position) => true;
        public float GroundHeight(Vector3 position) => 0;
        public bool IsBlocked(Vector3 from, Vector3 to, float radius) => Math.Abs(from.X - _origin.X) < 4 && Math.Abs(from.Z - _origin.Z) < 4;
        public bool IsRunwayClear(Airfield field) => true;
    }

    [Fact]
    public void ParachuteAtWorldCeilingStartsInsideBoundsAndDescendsImmediately()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", Vector3.UnitY * 511, Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Player, 1, "family"), 0);
        world.SetManualControl(plane.Id, 1, new FlightInput(0, 0, 0, 0, false));
        var environment = new FlatEnvironment();
        world.ExitPlayer(plane.Id, 1, environment);
        EjectedOccupant player = Assert.Single(world.Ejections);
        Assert.True(environment.IsInWorld(player.Position));
        float height = player.Position.Y;
        world.Tick(0.1f, environment);
        Assert.True(player.Position.Y < height);
    }

    [Fact]
    public void CrashAtWorldCeilingKeepsBallisticOccupantsInsideBounds()
    {
        var world = new AircraftWorld();
        Aircraft plane = world.BuildAircraft("family", Vector3.UnitY * 511, Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Player, 1, "family"), 0);
        world.SetManualControl(plane.Id, 1, new FlightInput(0, 0, 0, 0, false));
        var environment = new CeilingCrashEnvironment();
        world.Tick(0.01f, environment);
        Assert.Equal(FlightPhase.Wrecked, plane.Phase);
        Assert.True(environment.IsInWorld(Assert.Single(world.Ejections).Position));
    }

    private sealed class CeilingCrashEnvironment : IFlightEnvironment
    {
        public bool IsInWorld(Vector3 position) => new FlatEnvironment().IsInWorld(position);
        public float GroundHeight(Vector3 position) => 0;
        public bool IsBlocked(Vector3 from, Vector3 to, float radius) => radius > 1;
        public bool IsRunwayClear(Airfield field) => true;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.21f)]
    public void ParachuteLandsWhenNativeCollisionBlocksCrossingGround(float contactHeight)
    {
        AircraftWorld world = FlyingWorld(out Aircraft plane);
        world.ExitPlayer(plane.Id, 1, new FlatEnvironment());
        var environment = new DescentCollisionEnvironment(contactHeight);
        for (int step = 0; step < 500; step++) world.Tick(0.1f, environment);
        Assert.Empty(world.Ejections);
        Assert.Single(world.DrainEvents(), item => item.Kind == AircraftEventKind.OccupantLanded && item.Occupants[0].Id == 1);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(130)]
    public void MidairRoofCollisionDoesNotTeleportParachutistToGround(int seconds)
    {
        AircraftWorld world = FlyingWorld(out Aircraft plane);
        world.ExitPlayer(plane.Id, 1, new FlatEnvironment());
        var environment = new DescentCollisionEnvironment(100);
        for (int step = 0; step < seconds * 10; step++) world.Tick(0.1f, environment);
        EjectedOccupant player = Assert.Single(world.Ejections);
        Assert.True(player.Position.Y >= 100);
        Assert.DoesNotContain(world.DrainEvents(), item => item.Kind == AircraftEventKind.OccupantLanded && item.Occupants[0].Id == 1);
        Assert.InRange(player.Elapsed, 0, AircraftRules.ParachuteMaximumElapsed);
        using var bytes = new MemoryStream();
        WorldRecord.Write(bytes, world); bytes.Position = 0;
        AircraftWorld restored = WorldRecord.Read(bytes);
        EjectedOccupant savedPlayer = Assert.Single(restored.Ejections);
        Assert.Equal(player.Occupant.Owner, savedPlayer.Occupant.Owner);
        float blockedHeight = savedPlayer.Position.Y;
        restored.Tick(0.1f, new FlatEnvironment());
        Assert.Single(restored.Ejections);
        Assert.InRange(savedPlayer.Position.Y, 1, blockedHeight - 0.1f);
        for (int step = 0; step < 500; step++) restored.Tick(0.1f, new FlatEnvironment());
        Assert.Empty(restored.Ejections);
        Assert.Single(restored.DrainEvents(), item => item.Kind == AircraftEventKind.OccupantLanded && item.Occupants[0].Id == 1);
    }

    private sealed class DescentCollisionEnvironment : IFlightEnvironment
    {
        private readonly float _obstacleHeight;
        public DescentCollisionEnvironment(float obstacleHeight) { _obstacleHeight = obstacleHeight; }
        public bool IsInWorld(Vector3 position) => new FlatEnvironment().IsInWorld(position);
        public float GroundHeight(Vector3 position) => 0;
        public bool IsBlocked(Vector3 from, Vector3 to, float radius) => from.Y >= _obstacleHeight && to.Y <= _obstacleHeight;
        public bool IsRunwayClear(Airfield field) => true;
    }

    private static AircraftWorld FlyingWorld(out Aircraft plane)
    {
        var world = new AircraftWorld();
        plane = world.BuildAircraft("family", Vector3.UnitY * 150, Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Player, 1, "family"), 0);
        world.Board(plane.Id, new Occupant(OccupantKind.Child, 2, "family"), 1);
        world.SetManualControl(plane.Id, 1, new FlightInput(1, 0, 0, 0, false));
        for (int step = 0; step < 60; step++) world.Tick(0.05f, new FlatEnvironment());
        return world;
    }
}
