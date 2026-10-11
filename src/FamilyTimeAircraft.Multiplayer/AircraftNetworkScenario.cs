using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FamilyTimeAircraft.Core;
using FamilyTimeAircraft.Diagnostics;
using FamilyTimeAircraft.Rendering;
using FamilyTimeMultiplayer;
using FamilyTimeMultiplayer.Families;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Holds;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Core.Protocol;
using HarmonyLib;
using FamilyTimeMultiplayer.Sessions;
using GAT;
using UnityEngine;
using NVector = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace FamilyTimeAircraft.Multiplayer;

[DefaultExecutionOrder(10000)]
public sealed class AircraftNetworkScenario : MonoBehaviour
{
    private const float WaitTimeout = 75;
    private const float ControlInterval = 0.05f;
    private const float TaxiDuration = 2;
    private const float StopSpeed = 0.45f;
    private const float WalkReach = 7;
    private AircraftRuntime _runtime = null!;
    private int _planeId;
    private int _foreignPlaneId;
    private int _playerId;
    private int _foreignChildId;
    private readonly List<RigidTransform> _repairMaterials = new();
    private AircraftBuildingFixture? _impactBuilding;
    private NVector _initialPosition;
    private bool _host;
    private float? _remoteCursorFixtureAltitude;
    private bool ProbeRemoteCursor => Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-remote-cursor-smoke") >= 0;
    private bool ProbeEjection => Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-probe-ejection") >= 0;

    public void Initialize(AircraftRuntime runtime)
    {
        _runtime = runtime;
        StartCoroutine(Guarded(Run()));
    }

    private void LateUpdate()
    {
        Diagnostics.AircraftKeyboardFixture.ApplyNativeCameraAim();
        if (_host && _remoteCursorFixtureAltitude.HasValue) SetRemoteCursorFixturePose(_remoteCursorFixtureAltitude.Value);
    }

    private void SetRemoteCursorFixturePose(float altitude)
    {
        AccessTools.Property(typeof(Aircraft), nameof(Aircraft.Position)).SetValue(Plane(), _initialPosition + NVector.UnitY * altitude);
        AccessTools.Property(typeof(Aircraft), nameof(Aircraft.Velocity)).SetValue(Plane(), NVector.Zero);
        AccessTools.Property(typeof(Aircraft), nameof(Aircraft.IsOnGround)).SetValue(Plane(), false);
        AccessTools.Property(typeof(Aircraft), nameof(Aircraft.Phase)).SetValue(Plane(), FlightPhase.Manual);
    }

    private void OnDestroy() => Diagnostics.AircraftKeyboardFixture.ReleaseKeyboard();

    private IEnumerator Guarded(IEnumerator routine)
    {
        var pending = new Stack<IEnumerator>();
        pending.Push(routine);
        while (pending.Count > 0)
        {
            IEnumerator current = pending.Peek();
            bool advanced;
            try { advanced = current.MoveNext(); }
            catch (Exception error) { _runtime.SetNotice("[AircraftNetworkTest] diagnostic exception: " + error); throw; }
            if (!advanced) { pending.Pop(); continue; }
            if (current.Current is IEnumerator nested) pending.Push(nested);
            else yield return current.Current;
        }
    }

    private IEnumerator Run()
    {
        yield return Await("live-session", IsSessionReady);
        _host = MultiplayerController.Current!.Session is HostSession;
        if (!_host) Diagnostics.AircraftKeyboardFixture.EnsureKeyboard();
        _runtime.GetComponent<AircraftMenu>().enabled = false;
        yield return _host ? RunHost() : RunClient();
        Check("scenario-complete", true);
    }

    private bool IsSessionReady()
    {
        if (!AircraftRuntime.IsGameReady || _runtime.SubmitCommand == null) return false;
        ISession? session = MultiplayerController.Current?.Session;
        if (session is ClientSession client) return client.IsLive;
        return session is HostSession host && host.RemotePlayers.Any(player => player.Position.HasValue)
            && HostFamilyService.Current != null;
    }

    private IEnumerator RunHost()
    {
        HostSession host = (HostSession)MultiplayerController.Current!.Session!;
        _playerId = host.RemotePlayers.First(player => player.Position.HasValue).PlayerId;
        HostFamilyService families = HostFamilyService.Current!;
        yield return Await("starter-family", () => families.MembersOf(families.OwnerOfPlayer(_playerId)).Count >= 2);
        CreateFixtures(host, families);
        yield return PrepareDestinationRunway(families.OwnerOfPlayer(_playerId), _initialPosition);
        Check("host-fixtures-created", _runtime.World.Airfields.Count == 2);
        yield return Await("native-departure-runway-clear", () => _runtime.Environment.IsRunwayClear(_runtime.World.Airfields.First()));
        yield return Await("client-pilot-ack", () => HasPlayerPilot(Plane(), _playerId));
        if (ProbeRemoteCursor) { yield return VerifyRemoteCursorHost(); yield break; }
        yield return new WaitForSecondsRealtime(2);
        var commanders = (Dictionary<int, RemoteCommander>)AccessTools.Field(typeof(HostCommandHub), "_commanders").GetValue(HostCommandHub.Current);
        Check("client-f-pilot-no-follow", commanders[_playerId].ReleasableFollowerCount == 0);
        Check("seated-player-native-command-rejected", commanders[_playerId].ReleasableFollowerCount == 0);
        Check("seated-player-native-hold-rejected", families.MembersOf(families.OwnerOfPlayer(_playerId)).All(id => HostHoldRegistry.Current!.HolderOf(GatInterop.ToHandle(id)) == null));
        yield return Await("native-manual-motion", () => Plane().Phase == FlightPhase.Manual && Plane().Velocity.Length() > 0.3f);
        Check("native-manual-ground-taxi", Plane().Position.Y <= _initialPosition.Y + 1);
        yield return Await("client-disembark-ack", () => !Plane().Seats.Values.Any(occupant => occupant.Kind == OccupantKind.Player));
        LogChildCandidates(families);
        yield return Await("native-child-pilot", () => HasChildPilot(Plane()));
        yield return new WaitForSecondsRealtime(2);
        Check("reserved-child-hold-rejected", HostHoldRegistry.Current!.HolderOf(GatInterop.ToHandle((ushort)Plane().Seats[0].Id)) == null);
        yield return Await("client-passenger-ack", () => Plane().Seats.Values.Any(occupant => occupant.Kind == OccupantKind.Player));
        Check("client-f-passenger-no-follow", commanders[_playerId].ReleasableFollowerCount == 0);
        foreach (Airfield runway in _runtime.World.Airfields) LogRunwayClearance(runway);
        if (ProbeEjection)
        {
            AccessTools.Property(typeof(Aircraft), nameof(Aircraft.Phase)).SetValue(Plane(), FlightPhase.Manual);
            AccessTools.Property(typeof(Aircraft), nameof(Aircraft.Velocity)).SetValue(Plane(), -NVector.UnitY * 20);
        }
        if (!ProbeEjection)
        {
            yield return Await("destination-flight-started", () => Plane().Phase != FlightPhase.Parked);
            yield return Await("destination-flight-landed", () => Plane().Phase == FlightPhase.Parked && NVector.Distance(Plane().Position, _runtime.World.Airfields.Last().Position) < 5);
            CreateImpactBuilding();
        }
        yield return Await("client-crash-order", () => Plane().Phase != FlightPhase.Parked);
        if (!ProbeEjection) Check("child-pilot-autopilot", Plane().Phase != FlightPhase.Manual);
        yield return ObserveCrash();
        if (!ProbeEjection) VerifyImpactBuildingRemoved();
        SpawnRepairMaterials();
        yield return Await("client-repair-ack", () => Plane().Phase == FlightPhase.Parked);
        yield return null;
        Check("repair-materials-consumed", _repairMaterials.All(IsDeleted));
        Check("host-session-survives", !host.IsFinished && host.RemotePlayers.Count > 0);
        yield return Await("malicious-client-disconnected", () => host.RemotePlayers.All(player => player.PlayerId != _playerId));
        Check("foreign-child-state-unchanged", _runtime.World.FindAircraft(_foreignPlaneId).Seats[0].Id == _foreignChildId);
        Check("host-survives-rejection", !host.IsFinished);
    }

    private void LogChildCandidates(HostFamilyService families)
    {
        string owner = families.OwnerOfPlayer(_playerId);
        _runtime.SetNotice("[AircraftNetworkEvidence] aircraft=" + Plane().Position + " available=" + families.AvailableFor(owner));
        foreach (ushort id in families.MembersOf(owner))
        {
            Handle handle = GatInterop.ToHandle(id);
            Matrix4x4? matrix = World.gat.GetInstanceMatrixImmediate(handle);
            bool cached = World.mapManager.TryGetCachedPackedCell(id, out uint cell);
            _runtime.SetNotice("[AircraftNetworkEvidence] child=" + id + " matrix=" + matrix?.GetPosition() + " cached=" + cached + " cell=" + Cell.Unpack(cell) + " free=" + GameFamilyAccess.IsFreeWorker(World.commandOwnershipService, id));
        }
    }

    private void CreateFixtures(HostSession host, HostFamilyService families)
    {
        Check("fresh-aircraft-world", _runtime.World.Aircrafts.Count == 0);
        PrepareDryFlightEnvironment();
        NightParticipant peer = host.RemotePlayers.First(player => player.PlayerId == _playerId);
        NVector origin = GameFlightEnvironment.ToCore(peer.Position!.Value + Vector3.right * 2);
        origin.Y = _runtime.Environment.GroundHeight(origin);
        _initialPosition = origin;
        if (ProbeRemoteCursor) AircraftTestEnvironment.PrepareFlightCorridor(GameFlightEnvironment.ToUnity(origin));
        string owner = families.OwnerOfPlayer(_playerId);
        _planeId = _runtime.World.BuildAircraft(owner, origin, NQuaternion.Identity).Id;
        _foreignPlaneId = _runtime.World.BuildAircraft(families.HostOwner, origin + NVector.UnitX * 24, NQuaternion.Identity).Id;
        PrepareRunwayTerrain(_runtime.World.BuildAirfield(owner, origin, NQuaternion.Identity));
        PositionChildren(families.MembersOf(owner), origin);
        MoveMamasOutsideRunway(origin);
        ushort foreign = families.MembersOf(families.HostOwner).First();
        _foreignChildId = foreign;
        _runtime.World.Board(_foreignPlaneId, new Occupant(OccupantKind.Child, foreign, families.HostOwner), 0);
    }

    private static void PrepareRunwayTerrain(Airfield field) => Diagnostics.AircraftTestEnvironment.PrepareRunway(GameFlightEnvironment.ToUnity(field.Position));

    private static void PrepareDryFlightEnvironment() => Diagnostics.AircraftTestEnvironment.ClearWater();

    private void CreateImpactBuilding()
    {
        Vector3 target = GameFlightEnvironment.ToUnity(_initialPosition + NVector.UnitZ * 5);
        target.y = _runtime.Environment.GroundHeight(GameFlightEnvironment.ToCore(target));
        _impactBuilding = AircraftBuildingFixture.CreateAt(target);
        Check("impact-building-fixture-ready", _impactBuilding.TargetCollisionPresent && _impactBuilding.TargetRenderersPresent
            && _impactBuilding.NeighborCollisionPresent && _impactBuilding.NeighborRenderersPresent);
    }

    private AircraftBuildingFixture ExpectedImpactCollision()
    {
        Vector3 target = GameFlightEnvironment.ToUnity(_initialPosition + NVector.UnitZ * 5);
        target.y = _runtime.Environment.GroundHeight(GameFlightEnvironment.ToCore(target));
        Vector3Int grid = AircraftBuildingFixture.GridPosition(target);
        return AircraftBuildingFixture.InspectCollisionOnly(AircraftBuildingFixture.WallTemplateIndex, grid, AircraftBuildingFixture.NeighborGridPosition(target));
    }

    private bool ImpactBuildingPresent()
    {
        Vector3 target = GameFlightEnvironment.ToUnity(_initialPosition + NVector.UnitZ * 5);
        target.y = _runtime.Environment.GroundHeight(GameFlightEnvironment.ToCore(target));
        Vector3Int grid = AircraftBuildingFixture.GridPosition(target);
        return AircraftBuildingFixture.PartPresent(AircraftBuildingFixture.WallTemplateIndex, grid);
    }

    private bool ImpactBuildingsPresent()
    {
        if (!ImpactBuildingPresent()) return false;
        Vector3 target = GameFlightEnvironment.ToUnity(_initialPosition + NVector.UnitZ * 5);
        target.y = _runtime.Environment.GroundHeight(GameFlightEnvironment.ToCore(target));
        int template = AircraftBuildingFixture.WallTemplateIndex;
        Vector3Int targetGrid = AircraftBuildingFixture.GridPosition(target);
        Vector3Int neighborGrid = AircraftBuildingFixture.NeighborGridPosition(target);
        return AircraftBuildingFixture.PartPresent(template, targetGrid) && AircraftBuildingFixture.PartPresent(template, neighborGrid);
    }

    private void VerifyImpactBuildingRemoved()
    {
        Check("impact-building-removed-on-host", _impactBuilding != null && !_impactBuilding.TargetCollisionPresent && _impactBuilding.TargetRenderersRemoved);
        Check("impact-neighbor-retained-on-host", _impactBuilding != null && _impactBuilding.NeighborCollisionPresent && _impactBuilding.NeighborRenderersPresent);
    }

    private IEnumerator PrepareDestinationRunway(string owner, NVector origin)
    {
        if (ProbeEjection)
        {
            PrepareRunwayTerrain(_runtime.World.BuildAirfield(owner, origin + NVector.UnitZ * 160, NQuaternion.Identity));
            yield break;
        }
        const int candidateSpacing = 80;
        for (int row = 1; row <= 5; row++)
            for (int column = -4; column <= 4; column++)
            {
                NVector position = origin + new NVector(column * candidateSpacing, 0, row * candidateSpacing);
                Airfield candidate = new AircraftWorld().BuildAirfield(owner, position, NQuaternion.Identity);
                PrepareRunwayTerrain(candidate);
                yield return new WaitForSecondsRealtime(0.2f);
                if (!_runtime.Environment.IsRunwayClear(candidate)) continue;
                _runtime.World.BuildAirfield(owner, position, NQuaternion.Identity);
                Check("native-destination-runway-clear", true);
                yield break;
            }
        Check("native-destination-runway-clear", false);
        throw new InvalidOperationException("The diagnostic could not find a genuinely clear destination runway.");
    }

    private static void PositionChildren(IEnumerable<ushort> children, NVector origin)
    {
        int index = 0;
        foreach (ushort child in children)
        {
            NVector position = origin + new NVector(9 + index++ % 2, 0, -index % 3);
            position.Y = World.heightfield.GetWorldHeight(position.X, position.Z);
            World.gat.SetInstanceMatrix(GatInterop.ToHandle(child), Matrix4x4.TRS(GameFlightEnvironment.ToUnity(position), Quaternion.identity, Vector3.one), true, true, true);
        }
    }

    private static void MoveMamasOutsideRunway(NVector origin)
    {
        int index = 0;
        Type nativeGame = typeof(AircraftRuntime).Assembly.GetType("FamilyTimeAircraft.NativeAircraftGame")!;
        var rigids = (IEnumerable<RigidTransform>)AccessTools.Method(nativeGame, "Rigids").Invoke(null, null);
        foreach (WolfMama mama in UnityEngine.Object.FindObjectsByType<WolfMama>(FindObjectsSortMode.None))
        {
            AccessTools.Field(typeof(WolfMama), "allowFollow").SetValue(mama, false);
            Vector3 position = GameFlightEnvironment.ToUnity(origin + NVector.UnitX * (30 + index++ * 3));
            World.gat.SetInstanceMatrix(mama.FakeGATHandle, Matrix4x4.TRS(position, Quaternion.identity, Vector3.one), false, false, false);
            foreach (RigidTransform rigid in rigids)
                if (rigid.FakeGATHandle == mama.FakeGATHandle) { rigid.SetPosition(position); rigid.SetFrozen(true); }
        }
    }

    private void LogRunwayClearance(Airfield runway)
    {
        _runtime.SetNotice("[AircraftRunwayEvidence] runway=" + runway.Id + " clear=" + _runtime.Environment.IsRunwayClear(runway));
        for (int length = -36; length <= 36; length += 4)
            for (int width = -6; width <= 6; width += 3)
            {
                Vector3 point = GameFlightEnvironment.ToUnity(runway.Position) + new Vector3(width, 0, length);
                float ground = World.heightfield.GetWorldHeight(point.x, point.z);
                if (Mathf.Abs(ground - runway.Position.Y) > 0.35f)
                {
                    _runtime.SetNotice("[AircraftRunwayEvidence] point=" + point + " ground=" + ground + " elevation=" + runway.Position.Y);
                    return;
                }
                MethodInfo? waterQuery = AccessTools.Method(typeof(NavManager), "IsWaterNavigationCell", new[] { typeof(Vector3Int) });
                if (waterQuery != null && (bool)waterQuery.Invoke(World.navManager, new object[] { Vector3Int.RoundToInt(point) }))
                {
                    _runtime.SetNotice("[AircraftRunwayEvidence] point=" + point + " navigationWater=True");
                    return;
                }
                if (!World.Raycast(point + Vector3.up * 0.4f, Vector3.up, 3, out World.HitInfo hit)) continue;
                _runtime.SetNotice("[AircraftRunwayEvidence] point=" + point + " rigid=" + hit.rigidId + " block=" + hit.blockId + " type=" + hit.cpuCollisionType);
                return;
            }
    }

    private IEnumerator RunClient()
    {
        ClientSession client = (ClientSession)MultiplayerController.Current!.Session!;
        _playerId = client.LocalPlayerId;
        yield return Await("replicated-fixtures", () => _runtime.World.Aircrafts.Count == 2 && _runtime.World.Airfields.Count == 2);
        int[] ids = _runtime.World.Aircrafts.Select(plane => plane.Id).OrderBy(id => id).ToArray();
        _planeId = ids[0]; _foreignPlaneId = ids[1];
        _initialPosition = Plane().Position;
        int pilotFollowRequests = AircraftNativeInputProbe.FollowRequests;
        yield return Diagnostics.AircraftKeyboardFixture.ToggleSeat(Plane(), _runtime.Controls.Board.Value);
        yield return Await("client-pilot-ack", () => HasPlayerPilot(Plane(), _playerId));
        Check("client-f-pilot", HasPlayerPilot(Plane(), _playerId));
        Check("client-f-pilot-no-outbound-follow", AircraftNativeInputProbe.FollowRequests == pilotFollowRequests);
        yield return Await("client-avatar-seat", () => AvatarNear(Plane().SeatPosition(0), 2));
        yield return VerifySeatedMouseClicks();
        if (ProbeRemoteCursor) { yield return VerifyRemoteCursorClient(); yield break; }
        if (!ProbeEjection) yield return AttemptSeatedPlayerActions(client);
        yield return TaxiAndStop();
        int exitFootRequests = AircraftNativeFootPacketProbe.FootRequests;
        yield return Diagnostics.AircraftKeyboardFixture.ToggleSeat(Plane(), _runtime.Controls.Board.Value);
        yield return Await("client-disembark-ack", () => !Plane().Seats.Values.Any(occupant => occupant.Kind == OccupantKind.Player));
        Check("client-ground-exit-no-outbound-foot", AircraftNativeFootPacketProbe.FootRequests == exitFootRequests);
        Send(AircraftCommandKind.AssignChildPilot);
        yield return Await("native-child-pilot", () => HasChildPilot(Plane()));
        yield return AttemptReservedChildHold(client);
        int passengerFollowRequests = AircraftNativeInputProbe.FollowRequests;
        yield return Diagnostics.AircraftKeyboardFixture.ToggleSeat(Plane(), _runtime.Controls.Board.Value);
        yield return Await("client-passenger-ack", () => Plane().Seats.TryGetValue(1, out Occupant occupant) && occupant.Id == _playerId);
        Check("client-f-passenger", Plane().Seats.TryGetValue(1, out Occupant passenger) && passenger.Id == _playerId);
        Check("client-f-passenger-no-outbound-follow", AircraftNativeInputProbe.FollowRequests == passengerFollowRequests);
        if (!ProbeEjection)
        {
            yield return FlyToDestination();
            yield return Await("impact-building-replicated", ImpactBuildingsPresent);
            AircraftBuildingFixture fixture = ExpectedImpactCollision();
            _impactBuilding = fixture;
            _runtime.Dispatch(new AircraftCommand(AircraftCommandKind.CrashAt, _planeId, position: GameFlightEnvironment.ToCore(fixture.TargetPoint)));
        }
        yield return Await("client-crash-order", () => Plane().Phase != FlightPhase.Parked);
        yield return ObserveCrash();
        if (!ProbeEjection)
        {
            Check("impact-building-removed-on-client", _impactBuilding != null && !_impactBuilding.TargetCollisionPresent
                && !AircraftBuildingFixture.PartRenderersPresent(_impactBuilding.TemplateIndex, _impactBuilding.TargetPosition));
            Check("impact-neighbor-retained-on-client", _impactBuilding != null && _impactBuilding.NeighborCollisionPresent
                && AircraftBuildingFixture.PartRenderersPresent(_impactBuilding.TemplateIndex, _impactBuilding.NeighborPosition));
        }
        yield return WalkToWreck();
        Send(AircraftCommandKind.Repair);
        yield return Await("client-repair-ack", () => Plane().Phase == FlightPhase.Parked);
        Check("client-session-survives", client.IsLive && !client.IsFinished);
        yield return RejectForeignChild(client);
    }

    private IEnumerator VerifyRemoteCursorHost()
    {
        int previousReleases = AircraftRemoteCursorProbe.Releases;
        Vector3 intended = GameFlightEnvironment.ToUnity(_initialPosition) + new Vector3(5, 0, 15);
        Check("remote-cursor-native-target-walkable", World.navManager.IsWalkableSurface(Vector3Int.FloorToInt(intended)));
        yield return Await("remote-cursor-native-child-passenger", () => Plane().Seats.Values.Any(occupant => occupant.Kind == OccupantKind.Child));
        _remoteCursorFixtureAltitude = 80;
        SetRemoteCursorFixturePose(80);
        Check("remote-cursor-airborne", Plane().Position.Y > _initialPosition.Y + 50);
        yield return Await("remote-cursor-native-release", () => AircraftRemoteCursorProbe.Releases > previousReleases);
        Check("remote-cursor-host-ray-beyond-native-range", AircraftRemoteCursorResolveProbe.BeyondNativeRange);
        Check("remote-cursor-native-valid", AircraftRemoteCursorProbe.CursorValid);
        yield return Await("remote-cursor-canonical-parachute-order", () => _runtime.World.Ejections.Any(occupant => occupant.Occupant.Kind == OccupantKind.Child && occupant.HasParachute));
        Vector3 target = GameFlightEnvironment.ToUnity(_initialPosition) + new Vector3(5, 0, 15);
        Check("remote-cursor-target-matches", Vector3.Distance(AircraftRemoteCursorProbe.Target, target) < 2);
        var commanders = (Dictionary<int, RemoteCommander>)AccessTools.Field(typeof(HostCommandHub), "_commanders").GetValue(HostCommandHub.Current);
        object follow = AccessTools.Field(typeof(RemoteCommander), "_follow").GetValue(commanders[_playerId]);
        Check("remote-cursor-range-restored", (float)AccessTools.Field(typeof(FollowController), "_followCursorMaxDistance").GetValue(follow) == 40);
        int firstRelease = AircraftRemoteCursorProbe.Releases;
        _remoteCursorFixtureAltitude = 20;
        yield return Await("remote-cursor-in-range-release", () => AircraftRemoteCursorProbe.Releases > firstRelease);
        Check("remote-cursor-in-range-valid", AircraftRemoteCursorProbe.CursorValid && Vector3.Distance(AircraftRemoteCursorProbe.Target, target) < 2);
        Check("remote-cursor-in-range-restored", (float)AccessTools.Field(typeof(FollowController), "_followCursorMaxDistance").GetValue(follow) == 40);
        _remoteCursorFixtureAltitude = null;
    }

    private IEnumerator VerifyRemoteCursorClient()
    {
        Send(AircraftCommandKind.BoardChildren, 1);
        yield return Await("remote-cursor-native-child-passenger", () => Plane().Seats.Values.Any(occupant => occupant.Kind == OccupantKind.Child));
        yield return Await("remote-cursor-airborne", () => Plane().Position.Y >= _initialPosition.Y + 55);
        yield return Await("remote-cursor-avatar-airborne", () => AvatarNear(Plane().SeatPosition(0), 2));
        yield return new WaitForSecondsRealtime(0.3f);
        Vector3 target = GameFlightEnvironment.ToUnity(_initialPosition) + new Vector3(5, 0, 15);
        _runtime.SetNotice("[AircraftRemoteCursorEvidence] camera=" + Camera.main.transform.position + " target=" + target + " plane=" + Plane().Position);
        Check("remote-cursor-target-beyond-native-range", Vector3.Distance(Camera.main.transform.position, target) > 40);
        int ordinaryRequests = AircraftNativeFootPacketProbe.NonFollowRequests;
        yield return AircraftKeyboardFixture.GoToWithNativeFollow(target, nativeMouseAim: true);
        _runtime.SetNotice("[AircraftRemoteCursorEvidence] signedYaw=" + AircraftNativeFootPacketProbe.LastViewYaw + " signedPitch=" + AircraftNativeFootPacketProbe.LastViewPitch);
        Check("remote-cursor-signed-look-sent", Math.Abs(AircraftNativeFootPacketProbe.LastViewPitch) > 30);
        Check("remote-cursor-no-ordinary-foot-packets", AircraftNativeFootPacketProbe.NonFollowRequests == ordinaryRequests);
        yield return Await("remote-cursor-in-range-altitude", () => Plane().Position.Y < _initialPosition.Y + 25);
        yield return Await("remote-cursor-in-range-avatar", () => AvatarNear(Plane().SeatPosition(0), 2));
        yield return new WaitForSecondsRealtime(0.3f);
        Check("remote-cursor-target-within-native-range", Vector3.Distance(Camera.main.transform.position, target) < 40);
        yield return AircraftKeyboardFixture.GoToWithNativeFollow(target, nativeMouseAim: true);
        Check("remote-cursor-in-range-no-ordinary-foot-packets", AircraftNativeFootPacketProbe.NonFollowRequests == ordinaryRequests);
        yield return new WaitForSecondsRealtime(3);
    }

    private IEnumerator TaxiAndStop()
    {
        float deadline = Time.realtimeSinceStartup + TaxiDuration;
        while (Time.realtimeSinceStartup < deadline)
        {
            Control(new FlightInput(0.25f, 0, 0, 0, false));
            yield return new WaitForSecondsRealtime(ControlInterval);
        }
        Check("native-manual-motion", Plane().Phase == FlightPhase.Manual && Plane().Velocity.Length() > 0.3f);
        Check("native-manual-ground-taxi", Plane().Position.Y <= _initialPosition.Y + 1);
        deadline = Time.realtimeSinceStartup + 15;
        while (Plane().Velocity.Length() > StopSpeed && Time.realtimeSinceStartup < deadline)
        {
            Control(new FlightInput(0, 0, 0, 0, true));
            yield return new WaitForSecondsRealtime(ControlInterval);
        }
        Check("manual-brake-stops", Plane().Velocity.Length() <= StopSpeed);
    }

    private IEnumerator AttemptReservedChildHold(ClientSession client)
    {
        int child = Plane().Seats[0].Id;
        NVector position = Plane().SeatPosition(0);
        client.SendHold(new HoldMessage(HoldTarget.Agent((ushort)child), HoldPhase.Begin, true,
            new NetVector3(position.X, position.Y, position.Z), new NetQuaternion(0, 0, 0, 1), new NetVector3(0, 0, 0)));
        yield return new WaitForSecondsRealtime(3);
        Check("reserved-child-seat-preserved", Plane().Seats.TryGetValue(0, out Occupant pilot) && pilot.Id == child);
        Check("reserved-child-hold-session-survives", client.IsLive && !client.IsFinished);
    }

    private IEnumerator VerifySeatedMouseClicks()
    {
        Diagnostics.AircraftKeyboardFixture.SetMouseButtons(0);
        yield return new WaitForSecondsRealtime(Diagnostics.AircraftKeyboardFixture.HoldSeconds);
        int callbacks = Diagnostics.AircraftNativeClickProbe.ObservedCallbacks;
        int originals = Diagnostics.AircraftNativeClickProbe.ExecutedCallbacks;
        int packets = AircraftNativeFootPacketProbe.FootRequests;
        foreach (ushort buttons in new ushort[] { 1, 0, 2, 0 })
        {
            Diagnostics.AircraftKeyboardFixture.SetMouseButtons(buttons);
            yield return new WaitForSecondsRealtime(Diagnostics.AircraftKeyboardFixture.HoldSeconds);
        }
        Check("client-seated-mouse-callbacks-observed", Diagnostics.AircraftNativeClickProbe.ObservedCallbacks >= callbacks + 4);
        Check("client-seated-mouse-originals-suppressed", Diagnostics.AircraftNativeClickProbe.ExecutedCallbacks == originals);
        Check("client-seated-mouse-no-outbound-foot", AircraftNativeFootPacketProbe.FootRequests == packets);
    }

    private IEnumerator AttemptSeatedPlayerActions(ClientSession client)
    {
        yield return Await("seated-player-own-child-ready", () => ((ushort[])AccessTools.Field(typeof(LocalFamilyView), "_members").GetValue(null)).Length > 0);
        ushort child = ((ushort[])AccessTools.Field(typeof(LocalFamilyView), "_members").GetValue(null)).First();
        ushort local = ushort.MaxValue;
        yield return Await("seated-player-own-child-mapped", () => client.Identities.TryGetLocal(child, out local) && World.gat.GetInstanceMatrixImmediate(GatInterop.ToHandle(local)).HasValue);
        Vector3 position = World.gat.GetInstanceMatrixImmediate(GatInterop.ToHandle(local))!.Value.GetPosition();
        client.SendIntent(CommandIntentMessage.InteractAgent(child, new NetVector3(position.x, position.y, position.z)));
        client.SendHold(new HoldMessage(HoldTarget.Agent(child), HoldPhase.Begin, true,
            new NetVector3(position.x, position.y, position.z), new NetQuaternion(0, 0, 0, 1), new NetVector3(0, 0, 0)));
        yield return new WaitForSecondsRealtime(3);
        Check("seated-player-native-action-session-survives", client.IsLive && HasPlayerPilot(Plane(), _playerId));
    }

    private IEnumerator FlyToDestination()
    {
        Airfield destination = _runtime.World.Airfields.Last();
        _runtime.Dispatch(new AircraftCommand(AircraftCommandKind.FlightToAirfield, _planeId, destination.Id));
        yield return Await("destination-flight-started", () => Plane().Phase != FlightPhase.Parked);
        yield return Await("destination-flight-airborne", () => Plane().Position.Y > _initialPosition.Y + 5);
        yield return Await("destination-flight-landed", () => Plane().Phase == FlightPhase.Parked && NVector.Distance(Plane().Position, destination.Position) < 5);
        yield return new WaitForSecondsRealtime(1);
    }

    private IEnumerator RejectForeignChild(ClientSession client)
    {
        int foreign = _runtime.World.FindAircraft(_foreignPlaneId).Seats[0].Id;
        Send(AircraftCommandKind.AssignChildPilot, foreign);
        yield return Await("foreign-child-sender-disconnected", () => client.IsFinished);
    }

    private IEnumerator ObserveCrash()
    {
        yield return Await("wreck-replicated", () => Plane().Phase == FlightPhase.Wrecked);
        NVector crashTarget = _initialPosition + NVector.UnitZ * 5;
        crashTarget.Y = _runtime.Environment.GroundHeight(crashTarget);
        if (!ProbeEjection) Check("crash-target-reached", NVector.Distance(Plane().Position, crashTarget) <= 3);
        yield return Await("passengers-ejected", () => _runtime.World.Ejections.Count >= 2);
        EjectedOccupant[] passengers = _runtime.World.Ejections.ToArray();
        NVector first = passengers[0].Velocity;
        NVector second = passengers[1].Velocity;
        Check("ejection-distinct-directions", first.X * second.X + first.Z * second.Z < 0);
        if (_host)
        {
            VerifyEjectedChildReservationRestore(passengers);
            yield return VerifyEjectedChildDamageGuard(passengers);
        }
        yield return Await("explosion-rendered-once", ExplosionReady);
        CaptureExplosion();
        yield return Await("passengers-landed", () => _runtime.World.Ejections.Count == 0);
        Check("explosion-stays-once", UnityEngine.Object.FindObjectsByType<AircraftExplosion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length == 1);
    }

    private IEnumerator VerifyEjectedChildDamageGuard(EjectedOccupant[] passengers)
    {
        EjectedOccupant child = passengers.First(passenger => passenger.Occupant.Kind == OccupantKind.Child);
        Handle handle = _runtime.ResolveChildHandle!(child.Occupant.Id);
        GATUtilities.InfoDeltaResult? result = null;
        World.gatUtilities.ApplyHealthDelta(GameFlightEnvironment.ToUnity(child.Position), handle, handle, -1, 100, value => result = value);
        yield return Await("ejected-child-damage-callback", () => result.HasValue);
        Check("ejected-child-native-damage-blocked", !result!.Value.requestSucceeded && result.Value.effectiveDelta == 0);
    }

    private void VerifyEjectedChildReservationRestore(EjectedOccupant[] passengers)
    {
        int child = passengers.First(passenger => passenger.Occupant.Kind == OccupantKind.Child).Occupant.Id;
        using var saved = new MemoryStream();
        WorldRecord.Write(saved, _runtime.World);
        saved.Position = 0;
        _runtime.ApplySnapshot(WorldRecord.Read(saved));
        Check("ejected-child-restored-reservation", !GameFamilyAccess.IsFreeWorker(World.commandOwnershipService, checked((ushort)child)));
    }

    private void CaptureExplosion()
    {
        string directory = Path.Combine(Application.persistentDataPath, "ftmp_shots");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "aircraft-explosion-" + (_host ? "host" : "client") + ".png");
        ScreenCapture.CaptureScreenshot(path);
        _runtime.SetNotice("[AircraftNetworkEvidence] screenshot=" + path);
    }

    private static bool ExplosionReady()
    {
        AircraftExplosion[] effects = UnityEngine.Object.FindObjectsByType<AircraftExplosion>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (effects.Length != 1) return false;
        var effect = (AircraftExplosion)effects[0];
        ParticleSystem[] particles = effect.GetComponentsInChildren<ParticleSystem>();
        AudioSource sound = effect.GetComponent<AudioSource>();
        return particles.Length == 3 && particles.Select(system => system.name).Distinct().Count() == 3
            && particles.All(system => system.GetComponent<ParticleSystemRenderer>().sharedMaterial?.shader?.isSupported == true)
            && sound != null && sound.isPlaying;
    }

    private IEnumerator WalkToWreck()
    {
        FieldInfo movement = typeof(PlayerController).GetField("_movementDirectionXZ", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("Native player movement direction is unavailable.");
        float deadline = Time.realtimeSinceStartup + 30;
        try
        {
            while (!AvatarNear(Plane().Position, WalkReach) && Time.realtimeSinceStartup < deadline)
            {
                Vector3 delta = GameFlightEnvironment.ToUnity(Plane().Position) - World.playerController.CachedInstancePosition;
                Vector2 direction = new Vector2(delta.x, delta.z).normalized;
                movement.SetValue(World.playerController, direction);
                yield return null;
            }
        }
        finally { movement.SetValue(World.playerController, Vector2.zero); }
        Check("native-walk-to-repair", AvatarNear(Plane().Position, WalkReach));
    }

    private void SpawnRepairMaterials()
    {
        Type native = typeof(AircraftRuntime).Assembly.GetType("FamilyTimeAircraft.NativeAircraftGame")!;
        MethodInfo find = native.GetMethod("FindMaterialPrefab", BindingFlags.Public | BindingFlags.Static)!;
        MethodInfo spawn = native.GetMethod("SpawnRigid", BindingFlags.Public | BindingFlags.Static)!;
        foreach (ConstructionRequirement ingredient in Construction.AircraftBlueprints.RepairRecipe)
        {
            object prefab = find.Invoke(null, new object[] { ingredient.req });
            for (int count = 0; count < ingredient.amount; count++)
            {
                Vector3 position = GameFlightEnvironment.ToUnity(Plane().Position) + new Vector3(count % 3 - 1, 1, count / 3);
                var rigid = (RigidTransform)spawn.Invoke(null, new[] { prefab, (object)position });
                rigid.SetFrozen(true);
                _repairMaterials.Add(rigid);
            }
        }
        Check("repair-fixture-ready", _repairMaterials.Count == Construction.AircraftBlueprints.RepairRecipe.Sum(ingredient => ingredient.amount));
    }

    private static bool IsDeleted(RigidTransform rigid)
    {
        PropertyInfo? property = rigid.GetType().GetProperty("IsAlive", BindingFlags.Public | BindingFlags.Instance);
        if (property != null) return !(bool)property.GetValue(rigid);
        object boxed = rigid;
        if (boxed is UnityEngine.Object entity) return entity == null;
        Type native = typeof(AircraftRuntime).Assembly.GetType("FamilyTimeAircraft.NativeAircraftGame")!;
        var remaining = (IEnumerable)native.GetMethod("Rigids", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
        foreach (object candidate in remaining) if (ReferenceEquals(candidate, rigid)) return false;
        return true;
    }

    private bool AvatarNear(NVector position, float distance)
    {
        Matrix4x4? matrix = World.gat.GetInstanceMatrixImmediate(World.playerController.PossessedHandle);
        return matrix.HasValue && NVector.Distance(GameFlightEnvironment.ToCore(matrix.Value.GetPosition()), position) < distance;
    }

    private Aircraft Plane() => _runtime.World.FindAircraft(_planeId);
    private static bool HasPlayerPilot(Aircraft plane, int player) => plane.Seats.TryGetValue(0, out Occupant pilot) && pilot.Kind == OccupantKind.Player && pilot.Id == player;
    private static bool HasChildPilot(Aircraft plane) => plane.Seats.TryGetValue(0, out Occupant pilot) && pilot.Kind == OccupantKind.Child;
    private void Send(AircraftCommandKind kind, int target = 0) => _runtime.Dispatch(new AircraftCommand(kind, _planeId, target));
    private void Control(FlightInput input) => _runtime.Dispatch(new AircraftCommand(AircraftCommandKind.ManualControl, _planeId, input: input));

    private IEnumerator Await(string name, Func<bool> condition)
    {
        float deadline = Time.realtimeSinceStartup + WaitTimeout;
        float nextEvidence = Time.realtimeSinceStartup;
        while (!condition() && Time.realtimeSinceStartup < deadline)
        {
            if (_planeId > 0 && Time.realtimeSinceStartup >= nextEvidence)
            {
                Aircraft plane = Plane();
                _runtime.SetNotice("[AircraftFlightEvidence] " + name + " phase=" + plane.Phase + " position=" + plane.Position + " ground=" + _runtime.Environment.GroundHeight(plane.Position) + " velocity=" + plane.Velocity + " destination=" + plane.TargetAirfieldId);
                nextEvidence = Time.realtimeSinceStartup + 10;
                if (name == "destination-flight-landed" && plane.Phase == FlightPhase.Wrecked) break;
            }
            yield return null;
        }
        Check(name, condition());
    }

    private void Check(string name, bool passed)
    {
        _runtime.SetNotice("[AircraftNetworkTest] " + (_host ? "host " : "client ") + name + "=" + (passed ? "PASS" : "FAIL"));
        if (!passed) throw new InvalidOperationException("Aircraft multiplayer diagnostic failed: " + name);
    }
}
