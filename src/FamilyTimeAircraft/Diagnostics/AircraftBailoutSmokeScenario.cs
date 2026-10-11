using System;
using System.Collections;
using System.Linq;
using FamilyTimeAircraft.Core;
using GAT;
using UnityEngine;

namespace FamilyTimeAircraft.Diagnostics;

public sealed class AircraftBailoutSmokeScenario
{
    private const float BoardingTimeoutSeconds = 30;
    private const float FlightTimeoutSeconds = 60;
    private readonly AircraftRuntime _runtime;
    private Aircraft _plane = null!;

    public AircraftBailoutSmokeScenario(AircraftRuntime runtime) => _runtime = runtime;

    public IEnumerator Run()
    {
        _plane = _runtime.World.Aircrafts.First();
        Vector3 center = GameFlightEnvironment.ToUnity(_plane.Position);
        AircraftTestEnvironment.ClearWater();
        AircraftTestEnvironment.PrepareFlightCorridor(center);
        Vector3 viewpoint = center + new Vector3(6, 1, 0);
        World.playerController.TeleportTo(viewpoint, (center + Vector3.up - viewpoint).normalized);
        yield return new WaitForSecondsRealtime(0.3f);
        yield return AircraftKeyboardFixture.ToggleSeat(_plane, _runtime.Controls.Board.Value);
        Report("bailout-player-pilot-boarded", PlayerSeated());
        _runtime.Dispatch(new AircraftCommand(AircraftCommandKind.BoardChildren, _plane.Id, 1));
        yield return Await(() => _plane.Seats.Values.Any(occupant => occupant.Kind == OccupantKind.Child), BoardingTimeoutSeconds);
        Occupant child = _plane.Seats.Values.First(occupant => occupant.Kind == OccupantKind.Child);
        float initialHeight = _plane.Position.Y;
        float deadline = Time.realtimeSinceStartup + FlightTimeoutSeconds;
        while (_plane.Position.Y < initialHeight + 10 && _plane.Phase != FlightPhase.Wrecked && Time.realtimeSinceStartup < deadline)
        {
            _runtime.Dispatch(new AircraftCommand(AircraftCommandKind.ManualControl, _plane.Id, input: new FlightInput(1, 0.2f, 0, 0, false)));
            yield return null;
        }
        Report("bailout-native-airborne", _plane.Position.Y >= initialHeight + 10);
        var exitPosition = _plane.Position;
        yield return AircraftKeyboardFixture.ToggleSeat(_plane, _runtime.Controls.Board.Value);
        Report("bailout-configured-key-exits-airborne-player", !PlayerSeated());
        Report("bailout-player-parachute-deployed", _runtime.World.Ejections.Any(occupant => occupant.Occupant.Kind == OccupantKind.Player && occupant.Occupant.Id == _runtime.LocalPlayerId && occupant.HasParachute));
        Report("bailout-child-remains-aboard", _plane.Seats.Values.Any(occupant => occupant.Key == child.Key));
        Report("bailout-aircraft-unpiloted", _plane.Phase == FlightPhase.Unpiloted);
        yield return new WaitForSecondsRealtime(1);
        Report("bailout-aircraft-keeps-moving", System.Numerics.Vector3.Distance(exitPosition, _plane.Position) > 1);
        yield return Await(() => _plane.Phase == FlightPhase.Wrecked, FlightTimeoutSeconds);
        Report("bailout-unpiloted-aircraft-crashes", _plane.Phase == FlightPhase.Wrecked);
    }

    private bool PlayerSeated() => _plane.Seats.Values.Any(occupant => occupant.Kind == OccupantKind.Player && occupant.Id == _runtime.LocalPlayerId);

    private static IEnumerator Await(Func<bool> condition, float timeout)
    {
        float deadline = Time.realtimeSinceStartup + timeout;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        if (!condition()) throw new InvalidOperationException("Native aircraft bailout diagnostic timed out.");
    }

    private void Report(string name, bool passed)
    {
        _runtime.SetNotice("[AircraftTest] " + name + "=" + (passed ? "PASS" : "FAIL"));
        if (!passed) throw new InvalidOperationException("Native aircraft bailout check failed: " + name);
    }
}
