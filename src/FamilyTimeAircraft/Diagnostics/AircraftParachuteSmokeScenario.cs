using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FamilyTimeAircraft.Core;
using GAT;
using HarmonyLib;
using UnityEngine;
using NVector = System.Numerics.Vector3;

namespace FamilyTimeAircraft.Diagnostics;

public sealed class AircraftParachuteSmokeScenario
{
    private const float TimeoutSeconds = 100;
    private const int PassengerCount = 3;
    private readonly AircraftRuntime _runtime;
    private readonly Action<Vector3, Vector3> _setCaptureCamera;
    private Aircraft _plane = null!;
    private Occupant _pilot = null!;
    private int[] _children = Array.Empty<int>();
    private NVector _target;
    private Airfield _nearestReturnAirfield = null!;

    public AircraftParachuteSmokeScenario(AircraftRuntime runtime, Action<Vector3, Vector3> setCaptureCamera)
    { _runtime = runtime; _setCaptureCamera = setCaptureCamera; }

    public IEnumerator Run()
    {
        _plane = _runtime.World.Aircrafts.First();
        yield return PrepareAircraft();
        yield return FlyAndDrop();
        yield return VerifyLandings();
        Report("parachute-canopies-cleared-after-landing", Canopies().Count == 0);
    }

    private IEnumerator PrepareAircraft()
    {
        Vector3 origin = GameFlightEnvironment.ToUnity(_plane.Position);
        Vector3 destination = origin + Vector3.forward * 240;
        AircraftTestEnvironment.ClearWater();
        AircraftTestEnvironment.PrepareRunway(origin);
        AircraftTestEnvironment.PrepareRunway(destination);
        bool highAltitude = Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-parachute-high-altitude-smoke") >= 0;
        if (highAltitude) AircraftTestEnvironment.PrepareRunway(origin + new Vector3(0, 40, 120));
        yield return new WaitForSecondsRealtime(1);
        Airfield departure = _runtime.World.BuildAirfield(_plane.Owner, _plane.Position, System.Numerics.Quaternion.Identity);
        Airfield arrival = _runtime.World.BuildAirfield(_plane.Owner, GameFlightEnvironment.ToCore(destination), System.Numerics.Quaternion.Identity);
        Report("parachute-departure-runway-clear", _runtime.Environment.IsRunwayClear(departure));
        yield return PrepareNativeChildren(origin);
        _runtime.Dispatch(new AircraftCommand(AircraftCommandKind.AssignChildPilot, _plane.Id));
        yield return Await("parachute-native-child-pilot-boarded", () => _plane.Seats.TryGetValue(0, out Occupant pilot) && pilot.Kind == OccupantKind.Child);
        _pilot = _plane.Seats[0];
        _runtime.Dispatch(new AircraftCommand(AircraftCommandKind.BoardChildren, _plane.Id, PassengerCount));
        yield return Await("parachute-three-native-passengers-boarded", () => _plane.Seats.Count == PassengerCount + 1);
        _children = _plane.Seats.Where(seat => seat.Key > 0).Select(seat => seat.Value.Id).ToArray();
        Vector3 viewpoint = origin + new Vector3(6, 1, 0);
        World.playerController.TeleportTo(viewpoint, (origin + Vector3.up - viewpoint).normalized);
        yield return new WaitForSecondsRealtime(0.3f);
        yield return AircraftKeyboardFixture.ToggleSeat(_plane, _runtime.Controls.Board.Value);
        Report("parachute-player-passenger-boarded", _plane.Seats.Values.Any(occupant => occupant.Kind == OccupantKind.Player));
        _runtime.World.OrderFlight(_plane.Id, arrival.Id, _plane.Owner, _runtime.Environment);
        yield return Await("parachute-flight-airborne", () => highAltitude
            ? _plane.Position.Y - World.heightfield.GetWorldHeight(_plane.Position.X + 15, _plane.Position.Z) >= 60
            : _plane.Position.Y >= departure.Position.Y + 30);
    }

    private IEnumerator PrepareNativeChildren(Vector3 origin)
    {
        Handle[] children = World.commandOwnershipService.FindAvailableWorkers(World.gat.instanceCount, origin, 50, _ => true);
        _runtime.SetNotice("[AircraftParachuteEvidence] nativeAvailableChildren=" + children.Length);
        int missing = Math.Max(0, PassengerCount + 1 - children.Length);
        if (missing > 0)
        {
            var positions = Enumerable.Range(0, missing).Select(index => Matrix4x4.TRS(origin + new Vector3(6, 0, -8 + index * 4), Quaternion.identity, Vector3.one)).ToArray();
            World.gat.CreateInstances(positions, World.commandOwnershipService.WorkerTemplateIndex);
            yield return new WaitForSecondsRealtime(1);
            children = World.commandOwnershipService.FindAvailableWorkers(World.gat.instanceCount, origin, 50, _ => true);
        }
        Report("parachute-four-native-children-available", children.Length >= PassengerCount + 1);
        foreach (var child in children.Take(PassengerCount + 1).Select((handle, index) => new { handle, index }))
            World.gat.SetInstanceMatrix(child.handle, Matrix4x4.TRS(origin + new Vector3(6, 0, -8 + child.index * 4), Quaternion.identity, Vector3.one), true, true, true);
        yield return new WaitForSecondsRealtime(1);
    }

    private IEnumerator FlyAndDrop()
    {
        bool highAltitude = Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-parachute-high-altitude-smoke") >= 0;
        Vector3 target = GameFlightEnvironment.ToUnity(_plane.Position) + Vector3.right * (highAltitude ? 5 : 15);
        target.y = World.heightfield.GetWorldHeight(target.x, target.z);
        Report("parachute-intended-native-ground-walkable", World.navManager.IsWalkableSurface(Vector3Int.FloorToInt(target)));
        _target = GameFlightEnvironment.ToCore(target);
        _runtime.SetNotice("[AircraftParachuteEvidence] intendedTarget=" + target + " aircraft=" + _plane.Position
            + " verticalHeight=" + (_plane.Position.Y - target.y) + " cameraTargetDistance=" + Vector3.Distance(Camera.main.transform.position, target));
        _nearestReturnAirfield = _runtime.World.Airfields.Where(field => field.Owner == _plane.Owner).OrderBy(field => NVector.Distance(field.Position, _plane.Position)).First();
        int cursorReleases = AircraftNativeGoToProbe.CursorReleases;
        var follow = (FollowController)AccessTools.Field(typeof(CommandService), "_follow").GetValue(World.commandService);
        float originalRange = (float)AccessTools.Field(typeof(FollowController), "_followCursorMaxDistance").GetValue(follow);
        yield return AircraftKeyboardFixture.GoToWithNativeFollow(target);
        Report("parachute-native-cursor-range-restored", (float)AccessTools.Field(typeof(FollowController), "_followCursorMaxDistance").GetValue(follow) == originalRange);
        Report("parachute-real-f-native-cursor-released", AircraftNativeGoToProbe.CursorReleases > cursorReleases);
        Report("parachute-native-cursor-target-matches", AircraftNativeGoToProbe.LastCursorTarget.HasValue && Vector3.Distance(AircraftNativeGoToProbe.LastCursorTarget.Value, target) <= 3);
        yield return Await("parachute-three-passengers-deployed", () => _runtime.World.Ejections.Count(occupant => _children.Contains(occupant.Occupant.Id) && occupant.HasParachute) == PassengerCount);
        Report("parachute-child-pilot-kept-seat", _plane.Seats.TryGetValue(0, out Occupant pilot) && pilot.Key == _pilot.Key);
        yield return null;
        Report("parachute-three-canopies-rendered", Canopies().Count == PassengerCount);
        foreach (EjectedOccupant child in _runtime.World.Ejections.Where(occupant => _children.Contains(occupant.Occupant.Id)))
        {
            Report("parachute-canopy-attached-" + child.Occupant.Id, Canopies().TryGetValue(child.Occupant.Key, out GameObject canopy)
                && Vector3.Distance(canopy.transform.position, GameFlightEnvironment.ToUnity(child.Position) + Vector3.up * 0.9f) < 0.2f);
        }
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-parachute-visual-smoke") >= 0) yield return CaptureCanopies();
    }

    private IEnumerator CaptureCanopies()
    {
        Vector3 center = GameFlightEnvironment.ToUnity(_runtime.World.Ejections.First(occupant => _children.Contains(occupant.Occupant.Id)).Position);
        _setCaptureCamera(center, new Vector3(14, 7, 14));
        yield return new WaitForEndOfFrame();
        string directory = System.IO.Path.Combine(Application.persistentDataPath, "fta_shots");
        System.IO.Directory.CreateDirectory(directory);
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(directory, "aircraft-three-parachutes.png"));
    }

    private IEnumerator VerifyLandings()
    {
        yield return Await("parachute-passengers-landed", () => !_runtime.World.Ejections.Any(occupant => _children.Contains(occupant.Occupant.Id)));
        foreach (int id in _children) Report("parachute-native-child-near-target-" + id, ChildNear(id, 7));
        yield return Await("parachute-child-pilot-returned-airfield", () => _plane.Phase == FlightPhase.Parked && !_plane.Seats.Values.Any(occupant => occupant.Key == _pilot.Key));
        Report("parachute-pilot-returned-nearest-airfield", NVector.Distance(_plane.Position, _nearestReturnAirfield.Position) < 5);
        yield return Await("parachute-child-pilot-walked-to-native-target", () => ChildNear(_pilot.Id, 7));
    }

    private bool ChildNear(int id, float distance)
    {
        Handle handle = _runtime.ResolveChildHandle?.Invoke(id) ?? new Handle { stableIndex = checked((ushort)id) };
        Matrix4x4? matrix = World.gat.GetInstanceMatrixImmediate(handle);
        return matrix.HasValue && NVector.Distance(GameFlightEnvironment.ToCore(matrix.Value.GetPosition()), _target) < distance;
    }

    private IDictionary<string, GameObject> Canopies()
    {
        object scene = AccessTools.Field(typeof(AircraftRuntime), "_scene").GetValue(_runtime);
        object parachutes = AccessTools.Field(scene.GetType(), "_parachutes").GetValue(scene);
        return (IDictionary<string, GameObject>)AccessTools.Field(parachutes.GetType(), "_canopies").GetValue(parachutes);
    }

    private IEnumerator Await(string name, Func<bool> condition)
    {
        float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
        float nextEvidence = 0;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            if (Time.realtimeSinceStartup >= nextEvidence)
            {
                _runtime.SetNotice("[AircraftParachuteEvidence] awaiting=" + name + " phase=" + _plane.Phase + " position=" + _plane.Position + " seats=" + _plane.Seats.Count + " reserved=" + _runtime.ReservedChildIds.Count + " ejections=" + _runtime.World.Ejections.Count);
                foreach (EjectedOccupant occupant in _runtime.World.Ejections.Where(occupant => occupant.HasParachute))
                {
                    NVector next = occupant.Position + occupant.Velocity / 60;
                    _runtime.SetNotice("[AircraftParachuteEvidence] child=" + occupant.Occupant.Key + " position=" + occupant.Position + " target=" + occupant.ParachuteTarget + " elapsed=" + occupant.Elapsed
                        + " ground=" + _runtime.Environment.GroundHeight(occupant.Position) + " next=" + next + " blocked=" + _runtime.Environment.IsBlocked(occupant.Position, next, 0.3f));
                }
                nextEvidence = Time.realtimeSinceStartup + 5;
            }
            yield return null;
        }
        Report(name, condition());
    }

    private void Report(string name, bool passed)
    {
        _runtime.SetNotice("[AircraftTest] " + name + "=" + (passed ? "PASS" : "FAIL"));
        if (!passed) throw new InvalidOperationException("Native parachute diagnostic check failed: " + name);
    }
}
