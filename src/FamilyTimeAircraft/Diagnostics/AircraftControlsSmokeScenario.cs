using System;
using System.Collections;
using System.Linq;
using FamilyTimeAircraft.Core;
using GAT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace FamilyTimeAircraft.Diagnostics;

public sealed class AircraftControlsSmokeScenario
{
    private const float KeyHoldSeconds = 0.15f;
    private readonly AircraftRuntime _runtime;
    private Aircraft _plane = null!;

    public AircraftControlsSmokeScenario(AircraftRuntime runtime) => _runtime = runtime;

    public IEnumerator Run()
    {
        _plane = _runtime.World.Aircrafts.First();
        Vector3 center = GameFlightEnvironment.ToUnity(_plane.Position);
        AircraftTestEnvironment.ClearWater();
        AircraftTestEnvironment.PrepareFlightCorridor(center);
        yield return new WaitForSecondsRealtime(1);
        Vector3 viewpoint = center + new Vector3(6, 1, 0);
        World.playerController.TeleportTo(viewpoint, (center + Vector3.up - viewpoint).normalized);
        World.gat.SetInstanceMatrix(World.playerController.PossessedHandle, Matrix4x4.TRS(viewpoint, Quaternion.LookRotation(center + Vector3.up - viewpoint), Vector3.one), false, false, false);
        yield return new WaitForSecondsRealtime(0.3f);
        yield return AircraftKeyboardFixture.ToggleSeat(_plane, _runtime.Controls.Board.Value);
        bool boarded = _plane.Seats.TryGetValue(0, out Occupant pilot) && pilot.Kind == OccupantKind.Player && pilot.Id == _runtime.LocalPlayerId;
        Report("keyboard-board-key-boards-player-pilot", boarded);
        bool following = ((FollowController)AccessTools.Field(typeof(CommandService), "_follow").GetValue(World.commandService)).IsFollowing;
        Report("keyboard-board-key-does-not-start-native-follow", !following);
        if (!boarded || following) throw new InvalidOperationException("Configured aircraft boarding input failed.");
        yield return VerifyBrakeKey(_runtime.Controls.DecreaseThrottle.Value, "keyboard-throttle-down-applies-aircraft-brake");
        yield return VerifyBrakeKey(_runtime.Controls.Brake.Value, "keyboard-dedicated-brake-applies-aircraft-brake");
        yield return VerifyConsumedMouseClicks();
        foreach (Key key in new[] { Key.S, Key.W, Key.UpArrow, Key.DownArrow, Key.A, Key.D, Key.LeftArrow, Key.RightArrow, Key.Q, Key.E }) yield return VerifyAxis(key);
        yield return AircraftKeyboardFixture.ToggleSeat(_plane, _runtime.Controls.Board.Value);
        Report("keyboard-board-key-exits-grounded-player", !_plane.Seats.Values.Any(occupant => occupant.Kind == OccupantKind.Player && occupant.Id == _runtime.LocalPlayerId));
    }

    private IEnumerator VerifyConsumedMouseClicks()
    {
        AircraftKeyboardFixture.SetMouseButtons(0);
        yield return new WaitForSecondsRealtime(KeyHoldSeconds);
        int observed = AircraftNativeClickProbe.ObservedCallbacks;
        int executed = AircraftNativeClickProbe.ExecutedCallbacks;
        foreach (ushort buttons in new ushort[] { 1, 0, 2, 0 })
        {
            AircraftKeyboardFixture.SetMouseButtons(buttons);
            yield return new WaitForSecondsRealtime(KeyHoldSeconds);
        }
        Report("keyboard-seated-mouse-callbacks-observed", AircraftNativeClickProbe.ObservedCallbacks >= observed + 4);
        Report("keyboard-seated-mouse-originals-suppressed", AircraftNativeClickProbe.ExecutedCallbacks == executed);
    }

    private IEnumerator VerifyAxis(Key key)
    {
        AircraftKeyboardFixture.SetKeys(key);
        yield return new WaitForSecondsRealtime(KeyHoldSeconds);
        FlightInput input = _plane.CurrentInput;
        float value = key == Key.S || key == Key.W || key == Key.UpArrow || key == Key.DownArrow ? input.Pitch
            : key == Key.A || key == Key.D || key == Key.LeftArrow || key == Key.RightArrow ? input.Roll : input.Yaw;
        bool positive = key == Key.S || key == Key.DownArrow || key == Key.D || key == Key.RightArrow || key == Key.E;
        Report("keyboard-flight-axis-" + key, positive ? value > 0 : value < 0);
        AircraftKeyboardFixture.SetKeys();
        yield return new WaitForSecondsRealtime(KeyHoldSeconds);
    }

    private IEnumerator VerifyBrakeKey(Key key, string checkName)
    {
        AircraftKeyboardFixture.SetKeys(key);
        yield return new WaitForSecondsRealtime(KeyHoldSeconds);
        Report(checkName, _plane.CurrentInput.Brake && _plane.CurrentInput.Throttle == 0);
        AircraftKeyboardFixture.SetKeys();
        yield return new WaitForSecondsRealtime(KeyHoldSeconds);
    }

    private void Report(string name, bool passed) => _runtime.SetNotice("[AircraftTest] " + name + "=" + (passed ? "PASS" : "FAIL"));
}
