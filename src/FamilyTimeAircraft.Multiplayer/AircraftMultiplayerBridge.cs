using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using FamilyTimeAircraft.Core;
using FamilyTimeAircraft.Rendering;
using FamilyTimeMultiplayer;
using FamilyTimeMultiplayer.Addons;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Game;
using FamilyTimeMultiplayer.Sessions;
using GAT;
using HarmonyLib;
using UnityEngine;
using Vector3 = System.Numerics.Vector3;

namespace FamilyTimeAircraft.Multiplayer;

public sealed class AircraftMultiplayerBridge : MonoBehaviour, ISessionAddon, ISessionAddonActionPolicy
{
    private const double SnapshotInterval = 0.1;
    private const float CommandReach = 50;
    private const float RepairReach = 10;
    private const double LandingHandoffSeconds = 0.15;
    private AircraftRuntime _runtime = null!;
    private ISessionAddonContext? _context;
    private Harmony? _harmony;
    private readonly SnapshotReceiver _receiver = new SnapshotReceiver();
    private readonly Dictionary<int, double> _lastControlTime = new Dictionary<int, double>();
    private readonly Dictionary<int, (ClientPoseMessage Pose, double Expires)> _exitPoses = new Dictionary<int, (ClientPoseMessage, double)>();
    private long _snapshotSequence;
    private double _nextSnapshot;
    public AddonDescriptor Descriptor { get; private set; } = null!;

    public static void Register(AircraftRuntime runtime)
    {
        var bridge = runtime.gameObject.AddComponent<AircraftMultiplayerBridge>();
        bridge._runtime = runtime;
        string directory = Path.Combine(Path.GetDirectoryName(typeof(AircraftRuntime).Assembly.Location)!, "assets");
        bridge.Descriptor = new AddonDescriptor(Plugin.Guid, Plugin.Version, AssetFingerprint(directory));
        bridge._harmony = new Harmony(Plugin.Guid + ".multiplayer");
        bridge._harmony.PatchAll(typeof(AircraftMultiplayerBridge).Assembly);
        SessionAddonRegistry.Register(bridge);
        if (Environment.GetCommandLineArgs().Contains("mp-flight")) runtime.gameObject.AddComponent<AircraftNetworkScenario>().Initialize(runtime);
    }

    private static string AssetFingerprint(string directory)
    {
        using var stream = new MemoryStream();
        foreach (string name in AircraftModels.ModelNames)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(directory, name + ".fta"));
            stream.Write(bytes, 0, bytes.Length);
        }
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
    }

    public void OnSessionStarted(ISessionAddonContext context)
    {
        _context = context; _receiver.Reset(); _snapshotSequence = 0; _nextSnapshot = 0;
        _runtime.IsAuthoritative = context.IsHost;
        _runtime.LocalPlayerId = context.LocalPlayerId;
        _runtime.SubmitCommand = Submit;
        _runtime.SubmitTowerPlacement = request => context.SendToHost(AircraftWire.EncodeTowerPlacementRequest(request));
        _runtime.ResolveChildHandle = ResolveChild;
        _runtime.ResolvePlayerHandle = ResolvePlayer;
        _runtime.CanCommandChild = CanCommandChild;
        _runtime.TryClaimChild = context.TryClaimAgent;
        _runtime.ReleaseChild = context.ReleaseAgent;
        _runtime.CommandOwner = CurrentCommandOwner;
        _runtime.CommandPlayerId = CurrentCommandPlayerId;
        _runtime.PublishEvent = PublishEvent;
        _runtime.PublishTowerEvent = PublishTowerEvent;
        _runtime.IsMaterialAvailable = rigid => FamilyTimeMultiplayer.Holds.HostHoldRegistry.Current?.HolderOf(rigid) == null;
    }

    public void OnSessionStopped()
    {
        bool wasClient = _context?.IsHost == false;
        if (!wasClient && _context != null)
        {
            _runtime.World.ReleaseRemotePlayers(_context.LocalPlayerId, _runtime.Environment);
            _runtime.ResetBoardingForSolo();
        }
        _context = null; _receiver.Reset(); _lastControlTime.Clear(); _exitPoses.Clear();
        _runtime.SubmitCommand = null; _runtime.SubmitTowerPlacement = null; _runtime.ResolveChildHandle = null; _runtime.ResolvePlayerHandle = null;
        _runtime.CanCommandChild = null; _runtime.TryClaimChild = null; _runtime.ReleaseChild = null;
        _runtime.CommandOwner = null; _runtime.CommandPlayerId = null; _runtime.PublishEvent = null; _runtime.PublishTowerEvent = null;
        _runtime.IsMaterialAvailable = null;
        if (wasClient) _runtime.ClearWorld();
        _runtime.IsAuthoritative = true; _runtime.LocalPlayerId = 0; _runtime.Owner = AircraftRules.SharedOwner;
    }

    public void OnPeerReady(int playerId)
    {
        if (_context?.IsHost == true) SendSnapshot(playerId);
    }

    public void OnPeerLeft(int playerId)
    {
        if (_context?.IsHost != true) return;
        _runtime.World.PlayerDisconnected(playerId, _runtime.Environment);
        _runtime.CancelPendingBoardingForPlayer(playerId);
        _exitPoses.Remove(playerId);
    }

    public void OnWorldReloaded()
    {
        _receiver.Reset(); _snapshotSequence = 0; _nextSnapshot = 0; _lastControlTime.Clear(); _exitPoses.Clear();
    }

    private void LateUpdate()
    {
        ISessionAddonContext? context = _context;
        if (context == null || !AircraftRuntime.IsGameReady) return;
        _runtime.LocalPlayerId = context.LocalPlayerId;
        if (!context.IsHost) return;
        _runtime.Owner = context.GetFamilyOwner(context.LocalPlayerId);
        StopStaleControls(context.HostTime);
        if (context.HostTime < _nextSnapshot) return;
        _nextSnapshot = context.HostTime + SnapshotInterval;
        SendSnapshot(null);
    }

    private void Submit(AircraftCommand command)
    {
        ISessionAddonContext context = _context ?? throw new InvalidOperationException("Multiplayer session is unavailable.");
        if (context.IsHost) Execute(command, context.LocalPlayerId);
        else context.SendToHost(AircraftWire.EncodeCommand(command));
    }

    public void OnMessage(int sender, byte[] payload)
    {
        ISessionAddonContext context = _context ?? throw new InvalidDataException("Aircraft addon has no active session.");
        AircraftPacketKind kind = AircraftWire.Kind(payload);
        if (context.IsHost)
        {
            if (kind == AircraftPacketKind.TowerPlacementRequest)
            {
                RequestTowerPlacement(sender, AircraftWire.DecodeTowerPlacementRequest(payload));
                return;
            }
            if (kind != AircraftPacketKind.Command) throw new InvalidDataException("Clients cannot publish aircraft state.");
            try { Execute(AircraftWire.DecodeCommand(payload), sender); }
            catch (InvalidOperationException error) { context.SendToPlayer(sender, AircraftWire.EncodeNotice(error.Message)); }
            return;
        }
        if (sender != PlayerRosterMessage.HostPlayerId) throw new InvalidDataException("Aircraft state did not originate from the host.");
        ReceiveHostPacket(kind, payload);
    }

    private void ReceiveHostPacket(AircraftPacketKind kind, byte[] payload)
    {
        switch (kind)
        {
            case AircraftPacketKind.SnapshotChunk:
                WorldSnapshot? snapshot = _receiver.Accept(payload);
                if (snapshot != null) { _runtime.ApplySnapshot(snapshot.World); _runtime.ApplyBuildState(snapshot.NativeState); }
                break;
            case AircraftPacketKind.Event: ReceiveEvent(AircraftWire.DecodeEvent(payload)); break;
            case AircraftPacketKind.TowerEvent: _runtime.ApplyAuthoritativeTowerEvent(AircraftWire.DecodeTowerEvent(payload)); break;
            case AircraftPacketKind.Notice: _runtime.SetNotice(AircraftWire.DecodeNotice(payload)); break;
            default: throw new InvalidDataException("Host sent an unexpected aircraft packet.");
        }
    }

    private void Execute(AircraftCommand command, int playerId)
    {
        ISessionAddonContext context = _context!;
        Aircraft plane = _runtime.World.FindAircraft(command.AircraftId);
        RequireCommandAuthority(context, command, plane, playerId);
        _runtime.ExecuteCommand(command, playerId, context.GetFamilyOwner(playerId));
        if (command.Kind == AircraftCommandKind.ManualControl) _lastControlTime[plane.Id] = context.HostTime;
    }

    private static void RequireCommandAuthority(ISessionAddonContext context, AircraftCommand command, Aircraft plane, int playerId)
    {
        if (command.Kind == AircraftCommandKind.ManualControl) return;
        if (command.Kind == AircraftCommandKind.BoardPlayer || command.Kind == AircraftCommandKind.BoardChildren || command.Kind == AircraftCommandKind.AssignChildPilot || command.Kind == AircraftCommandKind.Repair)
            context.RequireNear(playerId, ToNet(plane.Position), command.Kind == AircraftCommandKind.Repair ? RepairReach : CommandReach);
        if (command.Kind == AircraftCommandKind.AssignChildPilot && command.TargetId > 0)
        {
            if (command.TargetId > ushort.MaxValue) throw new InvalidDataException("Invalid child pilot identity.");
            context.RequireOwnedAgent(playerId, (ushort)command.TargetId);
        }
    }

    private void SendSnapshot(int? recipient)
    {
        ISessionAddonContext context = _context!;
        byte[][] packets = AircraftWire.EncodeSnapshot(_runtime.World, context.HostTime, checked(++_snapshotSequence), nativeState: _runtime.CaptureBuildState());
        foreach (byte[] packet in packets)
            if (recipient.HasValue) context.SendToPlayer(recipient.Value, packet);
            else context.Broadcast(packet);
    }

    private void PublishEvent(AircraftEvent aircraftEvent)
    {
        if (_context?.IsHost != true) return;
        RecordLandingHandoff(aircraftEvent);
        _context.Broadcast(AircraftWire.EncodeEvent(aircraftEvent));
    }

    private void PublishTowerEvent(TowerEvent towerEvent)
    {
        if (_context?.IsHost == true) _context.Broadcast(AircraftWire.EncodeTowerEvent(towerEvent));
    }

    private void RequestTowerPlacement(int sender, TowerPlacementRequest request)
    {
        ISessionAddonContext context = _context!;
        var position = new Vector3(request.GridX, GAT.World.heightfield.GetWorldHeight(request.GridX, request.GridZ), request.GridZ);
        context.RequireNear(sender, ToNet(position), CommandReach);
        string owner = context.GetFamilyOwner(sender);
        if (!_runtime.RequestTowerPlacement(request, owner))
            context.SendToPlayer(sender, AircraftWire.EncodeNotice("Aster Twin Spires cannot be placed at this location."));
    }

    private void ReceiveEvent(AircraftEvent aircraftEvent)
    {
        RecordLandingHandoff(aircraftEvent);
        if (aircraftEvent.Kind == AircraftEventKind.OccupantLanded)
            foreach (Occupant occupant in aircraftEvent.Occupants) _runtime.World.ApplyAuthoritativeLanding(occupant);
        _runtime.RenderEvent(aircraftEvent);
    }

    private void RecordLandingHandoff(AircraftEvent aircraftEvent)
    {
        if (_context == null || aircraftEvent.Kind != AircraftEventKind.OccupantLanded) return;
        foreach (Occupant occupant in aircraftEvent.Occupants)
            if (occupant.Kind == OccupantKind.Player)
                _exitPoses[occupant.Id] = (new ClientPoseMessage(ToNet(aircraftEvent.Position), 0, 0, NetVector3.Zero), _context.HostTime + LandingHandoffSeconds);
    }

    private string CurrentCommandOwner()
    {
        ISessionAddonContext context = _context!;
        return context.GetFamilyOwner(CurrentCommandPlayerId());
    }

    private int CurrentCommandPlayerId() => FamilyTimeMultiplayer.WorldEdits.HostToolAuthority.CurrentPlayerId ?? CommanderContext.Active?.PlayerId ?? _context!.LocalPlayerId;

    private bool CanCommandChild(int playerId, int childId)
    {
        if (_context?.IsHost != true || childId < 0 || childId > ushort.MaxValue) return false;
        if (FamilyTimeMultiplayer.Holds.HostHoldRegistry.Current?.HolderOf(GatInterop.ToHandle((ushort)childId)) != null) return false;
        try { _context.RequireOwnedAgent(playerId, (ushort)childId); return true; }
        catch (InvalidDataException) { return false; }
    }

    private Handle ResolveChild(int hostId)
    {
        if (hostId < 0 || hostId > ushort.MaxValue) return Handle.Null;
        ClientSession? client = MultiplayerController.Current?.Session as ClientSession;
        if (client != null) return client.Identities.TryGetLocal((ushort)hostId, out ushort localId) ? GatInterop.ToHandle(localId) : Handle.Null;
        return GatInterop.ToHandle((ushort)hostId);
    }

    private Handle ResolvePlayer(int playerId)
    {
        if (playerId == _runtime.LocalPlayerId) return LocalPlayer.PossessedHandle;
        var entry = MultiplayerController.Current?.Session?.Roster.FirstOrDefault(player => player.PlayerId == playerId);
        return entry == null || entry.Value.PlayerId != playerId ? Handle.Null : ResolveChild(entry.Value.AvatarStableIndex);
    }

    private void StopStaleControls(double now)
    {
        foreach (Aircraft plane in _runtime.World.Aircrafts)
            if (plane.Phase == FlightPhase.Manual && _lastControlTime.TryGetValue(plane.Id, out double last) && now - last > 2)
                _runtime.World.RecoverUncontrolledAircraft(plane.Id, _runtime.Environment);
    }

    public bool TryGetAuthoritativePlayerPose(int playerId, out ClientPoseMessage pose)
    {
        foreach (Aircraft plane in _runtime.World.Aircrafts)
            foreach (KeyValuePair<int, Occupant> seat in plane.Seats)
                if (seat.Value.Kind == OccupantKind.Player && seat.Value.Id == playerId)
                {
                    Vector3 forward = Vector3.Transform(Vector3.UnitZ, plane.Rotation);
                    pose = new ClientPoseMessage(ToNet(plane.SeatPosition(seat.Key)), MathF.Atan2(forward.X, forward.Z) * 180 / MathF.PI, 0, ToNet(plane.Velocity)); return true;
                }
        foreach (EjectedOccupant passenger in _runtime.World.Ejections)
            if (passenger.Occupant.Kind == OccupantKind.Player && passenger.Occupant.Id == playerId)
            { pose = new ClientPoseMessage(ToNet(passenger.Position), 0, 0, ToNet(passenger.Velocity)); return true; }
        if (_context != null && _exitPoses.TryGetValue(playerId, out var exit))
        {
            if (_context.HostTime <= exit.Expires) { pose = exit.Pose; return true; }
            _exitPoses.Remove(playerId);
        }
        pose = null!; return false;
    }

    public bool CanPerformNativeAction(int playerId, INetMessage message) => message is CommandIntentMessage intent
        && (intent.Kind == CommandIntentKind.FollowPressed || intent.Kind == CommandIntentKind.FollowReleased)
        || !TryGetAuthoritativePlayerPose(playerId, out _);

    private static NetVector3 ToNet(Vector3 value) => new NetVector3(value.X, value.Y, value.Z);

    private void OnDestroy() => _harmony?.UnpatchSelf();
}
