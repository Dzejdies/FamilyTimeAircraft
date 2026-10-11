using System.Numerics;
using Xunit;

namespace FamilyTimeAircraft.Core.Tests;

public sealed class ManualFlightTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void BankingCreatesLateralLiftWithoutThrottleOrYaw(float roll)
    {
        AircraftWorld world = FlyingWorld(out Aircraft plane);
        world.SetManualControl(plane.Id, 1, new FlightInput(0, 0, 0, roll, false));
        for (int step = 0; step < 20; step++) world.Tick(0.05f, new FlatEnvironment());
        Assert.True(plane.Velocity.X * roll > 1, $"Bank {roll} produced lateral velocity {plane.Velocity.X}.");
        Assert.True(plane.Position.X * roll > 0.25f);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void PitchInputMovesTheNoseInTheRequestedVerticalDirection(float pitch)
    {
        AircraftWorld world = FlyingWorld(out Aircraft plane);
        world.SetManualControl(plane.Id, 1, new FlightInput(1, pitch, 0, 0, false));
        for (int step = 0; step < 20; step++) world.Tick(0.05f, new FlatEnvironment());
        Assert.True(Vector3.Transform(Vector3.UnitZ, plane.Rotation).Y * pitch > 0.3f);
    }

    private static AircraftWorld FlyingWorld(out Aircraft plane)
    {
        var world = new AircraftWorld();
        plane = world.BuildAircraft("family", new Vector3(0, 150, 0), Quaternion.Identity);
        world.Board(plane.Id, new Occupant(OccupantKind.Player, 1, "family"), 0);
        world.SetManualControl(plane.Id, 1, new FlightInput(1, 0, 0, 0, false));
        for (int step = 0; step < 80; step++) world.Tick(0.05f, new FlatEnvironment());
        return world;
    }
}
