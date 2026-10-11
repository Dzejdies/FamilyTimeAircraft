using System;
using System.Collections.Generic;
using BepInEx.Logging;
using FamilyTimeAircraft.Construction;
using FamilyTimeAircraft.Core;
using FamilyTimeAircraft.Rendering;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeAircraft;

public sealed partial class AircraftRuntime : MonoBehaviour
{
    public static AircraftRuntime? Current { get; private set; }
    public AircraftControls Controls { get; set; } = null!;
    public AircraftWorld World { get; private set; } = new();
    public IFlightEnvironment Environment { get; } = new GameFlightEnvironment();
    public AircraftBlueprints Blueprints { get; private set; } = null!;
    public AircraftConstructionProjects Projects { get; private set; } = null!;
    public bool IsAuthoritative { get; set; } = true;
    public string Owner { get; set; } = "shared";
    public int LocalPlayerId { get; set; }
    public Action<AircraftCommand>? SubmitCommand { get; set; }
    public Action<TowerPlacementRequest>? SubmitTowerPlacement { get; set; }
    public Func<int, int, bool>? CanCommandChild { get; set; }
    public Func<int, ushort, bool>? TryClaimChild { get; set; }
    public Action<int, ushort>? ReleaseChild { get; set; }
    public Func<int, Handle>? ResolveChildHandle { get; set; }
    public Func<int, Handle>? ResolvePlayerHandle { get; set; }
    public Func<string>? CommandOwner { get; set; }
    public Action<AircraftEvent>? PublishEvent { get; set; }
    public Action<TowerEvent>? PublishTowerEvent { get; set; }
    public Action? PublishState { get; set; }
    public Func<RigidTransform, bool>? IsMaterialAvailable { get; set; }
    public string Notice { get; private set; } = "";
    private AircraftModels _models = null!;
    private AircraftScene _scene = null!;
    private TowerScene _towerScene = null!;
    private AircraftBoarding _boarding = null!;
    private AircraftGroundOrders _groundOrders = null!;
    private AircraftBuildState _buildState = null!;
    private ManualLogSource _log = null!;
    private readonly HashSet<long> _renderedEvents = new();
    private readonly HashSet<int> _replicatedReservations = new();
    private bool _towerPartsDirty;

    public void Initialize(ManualLogSource log, string assetDirectory)
    {
        _log = log;
        _models = new AircraftModels(assetDirectory);
        _scene = new AircraftScene(_models, Environment);
        _towerScene = new TowerScene(_models);
        _boarding = new AircraftBoarding(this);
        _groundOrders = new AircraftGroundOrders(this);
        Blueprints = new AircraftBlueprints(_models, assetDirectory);
        Projects = new AircraftConstructionProjects(this);
        _buildState = new AircraftBuildState(this);
        Current = this;
        gameObject.AddComponent<AircraftPersistence>().Initialize(this);
        gameObject.AddComponent<AircraftMenu>().Initialize(this);
    }

    public static bool IsGameReady => GAT.World.gat != null && GAT.World.playerController != null
        && GAT.World.playerController.IsPossessing && GAT.World.worldSerializer != null
        && !GAT.World.worldSerializer.IsBusy && !GAT.World.worldSerializer.isLoadingInProgress;

    private void Update()
    {
        Blueprints.Register();
        if (!IsGameReady) return;
        if (_towerPartsDirty) SynchronizeTowerParts();
        _buildState.Tick();
        if (IsAuthoritative) _groundOrders.Tick();
    }

    private void LateUpdate()
    {
        if (!IsGameReady) return;
        _scene.Draw(World);
        _towerScene.Tick();
        MirrorOccupants();
    }

    private void FixedUpdate()
    {
        if (!IsAuthoritative || !IsGameReady) return;
        _boarding.Tick();
        World.Tick(Time.fixedDeltaTime, Environment);
        MirrorOccupants();
        PublishState?.Invoke();
        foreach (AircraftEvent aircraftEvent in World.DrainEvents())
        {
            DeliverEvent(aircraftEvent);
        }
    }

    private void DeliverEvent(AircraftEvent aircraftEvent)
    {
        try
        {
            PublishEvent?.Invoke(aircraftEvent);
            RenderEvent(aircraftEvent);
        }
        catch (Exception exception)
        {
            _log.LogError($"Aircraft event {aircraftEvent.Kind} #{aircraftEvent.Sequence} failed: {exception}");
            throw;
        }
    }

    public void Dispatch(AircraftCommand command)
    {
        try
        {
            if (SubmitCommand != null) SubmitCommand(command);
            else ExecuteCommand(command, LocalPlayerId, LocalActorOwner(command));
            Notice = "";
        }
        catch (InvalidOperationException error) { SetNotice(error.Message); }
    }

    private string LocalActorOwner(AircraftCommand command) => World.FindAircraft(command.AircraftId).Owner;

    public void ResetBoardingForSolo()
    {
        _boarding.Dispose();
        if (IsAuthoritative && IsGameReady) _boarding.RestoreSeats();
    }

    public void SetNotice(string notice)
    {
        Notice = notice;
        _log.LogInfo("[Aircraft] " + notice);
    }

    public bool ExecuteCommand(AircraftCommand command, int playerId, string owner)
    {
        if (!IsAuthoritative) throw new InvalidOperationException("Only the host can change an aircraft.");
        Aircraft plane = World.FindAircraft(command.AircraftId);
        switch (command.Kind)
        {
            case AircraftCommandKind.BoardPlayer: BoardPlayer(plane, playerId, owner, command.TargetId); break;
            case AircraftCommandKind.BoardChildren: BoardChildren(plane, playerId, owner, command.TargetId); break;
            case AircraftCommandKind.AssignChildPilot: BoardChildPilot(plane, playerId, owner, command.TargetId); break;
            case AircraftCommandKind.ExitPlayer: World.ExitPlayer(plane.Id, playerId, Environment); break;
            case AircraftCommandKind.ParachuteDrop: OrderAircraftGoTo(plane, playerId, owner, command.Position); break;
            case AircraftCommandKind.Disembark: Disembark(plane, playerId, owner); break;
            case AircraftCommandKind.FlightToAirfield: World.OrderFlight(plane.Id, command.TargetId, owner, Environment); break;
            case AircraftCommandKind.CrashAt: World.OrderCrash(plane.Id, command.Position, owner, Environment); break;
            case AircraftCommandKind.ManualControl: World.SetManualControl(plane.Id, playerId, command.Input); break;
            case AircraftCommandKind.Repair: Repair(plane, playerId, owner); break;
            default: throw new InvalidOperationException("Unknown aircraft command.");
        }
        return true;
    }

    private void BoardPlayer(Aircraft plane, int playerId, string owner, int seat)
    {
        Handle handle = PlayerHandle(playerId);
        Matrix4x4? matrix = GAT.World.gat.GetInstanceMatrixImmediate(handle);
        if (!matrix.HasValue || Vector3.Distance(matrix.Value.GetPosition(), GameFlightEnvironment.ToUnity(plane.Position)) > 10)
            throw new InvalidOperationException("Move closer to the aircraft before boarding.");
        if (seat == AircraftRules.AutomaticBoardingSeat)
            seat = AircraftRules.ResolveAutomaticBoardingSeat(plane, owner, _boarding.IsSeatReserved(plane.Id, 0));
        if (seat < 0) seat = FindFreeSeat(plane, 1);
        if (seat < 0 || _boarding.IsSeatReserved(plane.Id, seat)) throw new InvalidOperationException("This seat is reserved for a boarding child.");
        World.Board(plane.Id, new Occupant(OccupantKind.Player, playerId, owner), seat);
    }

    private void BoardChildren(Aircraft plane, int playerId, string owner, int count)
    {
        RequireOwner(plane, owner);
        Handle[] children = GAT.World.commandOwnershipService.FindAvailableWorkers(GAT.World.gat.instanceCount,
            GameFlightEnvironment.ToUnity(plane.Position), 50, child => CanCommandChild?.Invoke(playerId, child.stableIndex) ?? true);
        int boarded = 0;
        foreach (Handle child in children)
        {
            int seat = FindFreeSeat(plane, 1);
            if (seat < 0 || boarded >= (count > 0 ? Math.Min(count, 8) : 8)) break;
            if (_boarding.Begin(plane.Id, playerId, owner, child, seat)) boarded++;
        }
        if (boarded == 0) throw new InvalidOperationException("No available children, seats, or accessible boarding route.");
    }

    private void BoardChildPilot(Aircraft plane, int playerId, string owner, int childId)
    {
        RequireOwner(plane, owner);
        Handle child = childId > 0 ? ChildHandle(childId) : FindPilotCandidate(plane, playerId);
        if (!NativeAircraftGame.IsAvailableChild(child)) throw new InvalidOperationException("The selected agent is not an available child worker.");
        if (plane.Seats.ContainsKey(0) || _boarding.IsSeatReserved(plane.Id, 0) || !_boarding.Begin(plane.Id, playerId, owner, child, 0))
            throw new InvalidOperationException("A child pilot cannot be assigned.");
    }

    private Handle FindPilotCandidate(Aircraft plane, int playerId)
    {
        Handle[] children = GAT.World.commandOwnershipService.FindAvailableWorkers(GAT.World.gat.instanceCount,
            GameFlightEnvironment.ToUnity(plane.Position), 50, child => CanCommandChild?.Invoke(playerId, child.stableIndex) ?? true);
        if (children.Length == 0) throw new InvalidOperationException("No available child pilot nearby.");
        return children[0];
    }

    private int FindFreeSeat(Aircraft plane, int minimum)
    {
        for (int seat = minimum; seat <= 8; seat++)
            if (!plane.Seats.ContainsKey(seat) && !_boarding.IsSeatReserved(plane.Id, seat)) return seat;
        return -1;
    }

    private void Disembark(Aircraft plane, int playerId, string owner)
    {
        var occupants = new List<Occupant>(plane.Seats.Values);
        foreach (Occupant occupant in occupants)
        {
            if (occupant.Kind == OccupantKind.Player && occupant.Id != playerId) continue;
            if (occupant.Kind == OccupantKind.Child && owner != plane.Owner && plane.Owner != "shared") continue;
            World.Disembark(plane.Id, occupant.Kind, occupant.Id, Environment);
        }
    }

    private void Repair(Aircraft plane, int playerId, string owner)
    {
        RequireOwner(plane, owner);
        if (plane.Phase != FlightPhase.Wrecked) throw new InvalidOperationException("This aircraft does not need repair.");
        Vector3 center = GameFlightEnvironment.ToUnity(plane.Position);
        Matrix4x4? playerMatrix = GAT.World.gat.GetInstanceMatrixImmediate(PlayerHandle(playerId));
        if (!playerMatrix.HasValue || Vector3.Distance(playerMatrix.Value.GetPosition(), center) > 10)
            throw new InvalidOperationException("Move closer to the wreck before repairing it.");
        var materials = new List<RigidTransform>();
        foreach (RigidTransform rigid in NativeAircraftGame.Rigids())
            if (!rigid.IsCarried && (IsMaterialAvailable?.Invoke(rigid) ?? true) && Vector3.Distance(rigid.position, center) < 10) materials.Add(rigid);
        List<RigidTransform> selected = SelectRepairMaterials(materials);
        World.Repair(plane.Id, owner);
        foreach (RigidTransform rigid in selected) NativeAircraftGame.DeleteRigid(rigid);
    }

    private static List<RigidTransform> SelectRepairMaterials(List<RigidTransform> candidates)
    {
        var selected = new List<RigidTransform>();
        foreach (ConstructionRequirement requirement in AircraftBlueprints.RepairRecipe)
            for (int count = 0; count < requirement.amount; count++)
            {
                RigidTransform? material = candidates.Find(rigid => rigid.SelfMaterial == requirement.req && !selected.Contains(rigid));
                if (material == null) throw new InvalidOperationException("Repair requires 8 logs and 4 stones near the wreck.");
                selected.Add(material);
            }
        return selected;
    }

    private static void RequireOwner(Aircraft plane, string owner) => AircraftRules.RequireOwner(plane, owner);

    public bool CompleteBuildPlan(BuildPlanManager manager, BuildPlanManager.BuildPlan plan)
    {
        if (!IsAuthoritative || !manager.IsBuildPlanActive(plan)) return false;
        BuildPart template = GAT.World.buildPartManager.BuildPartTemplates[plan.templateIndex];
        Matrix4x4 placement = template.GetPlacementMatrix(plan.gridPosition, plan.rotation);
        string owner = Projects.Require(plan).Owner;
        if (plan.templateIndex == Blueprints.AircraftIndex) World.BuildAircraft(owner, GameFlightEnvironment.ToCore(placement.GetPosition()), GameFlightEnvironment.ToCore(placement.rotation));
        else if (plan.templateIndex == Blueprints.TowerPairIndex)
        {
            if (World.TowerPairs.Count >= TowerLayout.MaximumTowerPairs ||
                !((GameFlightEnvironment)Environment).CanPlaceTowerPair(GameFlightEnvironment.ToCore(placement.GetPosition()), GameFlightEnvironment.ToCore(placement.rotation)))
            {
                SetNotice("Aster Twin Spires require flat, dry and unobstructed terrain.");
                return false;
            }
            if (!CreateTowerSectionParts(plan.gridPosition, plan.rotation))
            {
                SetNotice("The tower sections could not be placed at this location.");
                return false;
            }
            try { World.BuildTowerPair(owner, GameFlightEnvironment.ToCore(placement.GetPosition()), GameFlightEnvironment.ToCore(placement.rotation)); }
            catch { RemoveTowerSectionParts(plan.gridPosition, plan.rotation); throw; }
        }
        else
        {
            if (!((GameFlightEnvironment)Environment).CanPlaceRunway(GameFlightEnvironment.ToCore(placement.GetPosition()), GameFlightEnvironment.ToCore(placement.rotation)))
            {
                SetNotice("An airfield requires flat, dry and unobstructed terrain.");
                return false;
            }
            World.BuildAirfield(owner, GameFlightEnvironment.ToCore(placement.GetPosition()), GameFlightEnvironment.ToCore(placement.rotation));
        }
        AccessTools.Method(typeof(BuildPlanManager), "ApplyTerrainEffects").Invoke(null, new object[] { plan });
        manager.RemoveBuildPlan(plan);
        Projects.Remove(plan);
        _log.LogInfo("[Aircraft] native construction completed: " + template.displayName);
        return true;
    }

    private void MirrorOccupants()
    {
        foreach (Aircraft plane in World.Aircrafts)
            foreach (KeyValuePair<int, Occupant> seat in plane.Seats)
                PoseOccupant(seat.Value, plane.SeatPosition(seat.Key), plane.Rotation);
        foreach (EjectedOccupant ejected in World.Ejections)
            PoseOccupant(ejected.Occupant, ejected.Position, System.Numerics.Quaternion.Identity, ejected.Velocity);
    }

    private void PoseOccupant(Occupant occupant, System.Numerics.Vector3 position, System.Numerics.Quaternion rotation, System.Numerics.Vector3 velocity = default)
    {
        Handle handle = OccupantHandle(occupant);
        if (handle.isNull || !GAT.World.gat.IsHandleValid(handle)) return;
        GAT.World.gat.SetInstanceMatrix(handle, Matrix4x4.TRS(GameFlightEnvironment.ToUnity(position), GameFlightEnvironment.ToUnity(rotation), Vector3.one), false, false, false);
        GAT.World.gat.SetInstanceVelocity(handle, GameFlightEnvironment.ToUnity(velocity));
    }

    private Handle OccupantHandle(Occupant occupant) => occupant.Kind == OccupantKind.Child ? ChildHandle(occupant.Id) : PlayerHandle(occupant.Id);
    private Handle ChildHandle(int id) => ResolveChildHandle?.Invoke(id) ?? new Handle { stableIndex = checked((ushort)id) };
    private Handle PlayerHandle(int id) => ResolvePlayerHandle?.Invoke(id) ?? (id == LocalPlayerId ? GAT.World.playerController.PossessedHandle : Handle.Null);

    public bool IsChildReserved(int hostId)
    {
        if (_boarding.Contains(hostId) || !IsAuthoritative && _replicatedReservations.Contains(hostId)) return true;
        foreach (Aircraft aircraft in World.Aircrafts)
            foreach (Occupant occupant in aircraft.Seats.Values)
                if (occupant.Kind == OccupantKind.Child && occupant.Id == hostId) return true;
        foreach (EjectedOccupant ejected in World.Ejections)
            if (ejected.Occupant.Kind == OccupantKind.Child && ejected.Occupant.Id == hostId) return true;
        return false;
    }

    public bool IsNativeChildReserved(Handle handle)
    {
        foreach (int id in _boarding.ChildIds) if (ChildHandle(id) == handle) return true;
        if (!IsAuthoritative)
            foreach (int id in _replicatedReservations) if (ChildHandle(id) == handle) return true;
        foreach (Aircraft aircraft in World.Aircrafts)
            foreach (Occupant occupant in aircraft.Seats.Values)
                if (occupant.Kind == OccupantKind.Child && ChildHandle(occupant.Id) == handle) return true;
        foreach (EjectedOccupant ejected in World.Ejections)
            if (ejected.Occupant.Kind == OccupantKind.Child && ChildHandle(ejected.Occupant.Id) == handle) return true;
        return false;
    }

    public IReadOnlyCollection<int> ReservedChildIds => _boarding.ChildIds;
    public void ApplyReservedChildIds(IReadOnlyList<int> ids)
    {
        _replicatedReservations.Clear();
        if (IsAuthoritative) return;
        foreach (int id in ids) _replicatedReservations.Add(id);
    }

    public void CancelPendingBoardingForPlayer(int playerId) => _boarding.CancelPendingForPlayer(playerId);

    public byte[] CaptureBuildState() => _buildState.Capture();
    public void ApplyBuildState(byte[] data) => _buildState.Apply(data, false);
    public void RestoreBuildState(byte[] data) => _buildState.Apply(data, IsAuthoritative);

    public bool RequestTowerPlacement(TowerPlacementRequest request, string owner)
    {
        if (!IsAuthoritative || World.TowerPairs.Count >= TowerLayout.MaximumTowerPairs || !IsGameReady) return false;
        Vector3Int position = new(request.GridX, Mathf.RoundToInt(GAT.World.heightfield.GetWorldHeight(request.GridX, request.GridZ)), request.GridZ);
        BuildPart.Rotation rotation = (BuildPart.Rotation)request.QuarterTurns;
        if (!((GameFlightEnvironment)Environment).CanPlaceTowerPair(GameFlightEnvironment.ToCore(position), RotationFromQuarterTurns(request.QuarterTurns)))
            return false;
        if (!GAT.World.buildPlanManager.CreateBuildPlan(Blueprints.TowerPairIndex, position, rotation, 0.01f)) return false;
        BuildPlanManager.BuildPlan plan = FindTowerBuildPlan(position, rotation);
        Projects.Track(plan, owner);
        return true;
    }

    private BuildPlanManager.BuildPlan FindTowerBuildPlan(Vector3Int position, BuildPart.Rotation rotation)
    {
        var plans = (List<BuildPlanManager.BuildPlan>)AccessTools.Field(typeof(BuildPlanManager), "activeBuildPlans").GetValue(GAT.World.buildPlanManager);
        return plans.Find(plan => plan.templateIndex == Blueprints.TowerPairIndex && plan.gridPosition == position && plan.rotation == rotation)
            ?? throw new InvalidOperationException("Created tower project is missing from the native build manager.");
    }

    private static System.Numerics.Quaternion RotationFromQuarterTurns(byte quarterTurns) =>
        System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, quarterTurns * Mathf.PI * 0.5f);

    public void ApplySnapshot(AircraftWorld world)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        if (IsAuthoritative) { _groundOrders.Clear(); _boarding.Dispose(); }
        World = world;
        _towerPartsDirty = true;
        if (IsAuthoritative) _boarding.RestoreSeats();
    }

    public void ApplySavedSnapshot(AircraftWorld world)
    {
        if (IsAuthoritative) world.ReleaseRemotePlayers(LocalPlayerId, Environment);
        ApplySnapshot(world);
    }

    public void RenderEvent(AircraftEvent aircraftEvent)
    {
        if (!_renderedEvents.Add(aircraftEvent.Sequence)) return;
        switch (aircraftEvent.Kind)
        {
            case AircraftEventKind.Explosion: RenderExplosion(aircraftEvent); break;
            case AircraftEventKind.GroundGoTo: StartGroundOrders(aircraftEvent); break;
            case AircraftEventKind.OccupantLanded: LandOccupants(aircraftEvent); break;
            default: throw new InvalidOperationException("Unknown aircraft event.");
        }
    }

    public IReadOnlyList<TowerEvent> ApplyTowerImpact(int pairId, int sectionId, float speed, System.Numerics.Vector3 direction,
        System.Numerics.Vector3 surfaceNormal, long impactSequence)
    {
        IReadOnlyList<TowerEvent> events = World.ApplyTowerImpact(pairId, sectionId, speed, direction, surfaceNormal, impactSequence);
        foreach (TowerEvent towerEvent in events)
        {
            TowerPair pair = World.FindTowerPair(towerEvent.TowerPairId);
            ApplyTowerPresentation(pair, towerEvent);
            PublishTowerEvent?.Invoke(towerEvent);
        }
        return events;
    }

    public bool ApplyAuthoritativeTowerEvent(TowerEvent towerEvent)
    {
        if (!World.ApplyTowerEvent(towerEvent)) return false;
        ApplyTowerPresentation(World.FindTowerPair(towerEvent.TowerPairId), towerEvent);
        return true;
    }

    private void ApplyTowerPresentation(TowerPair pair, TowerEvent towerEvent)
    {
        if (towerEvent.Kind == TowerEventKind.SectionFailed)
        {
            int template = Blueprints.TowerSectionTemplateIndex(towerEvent.TowerIndex,
                towerEvent.SectionId % TowerLayout.SectionsPerTower);
            Vector3Int anchor = Vector3Int.RoundToInt(GameFlightEnvironment.ToUnity(pair.Position));
            Vector3Int position = SectionGridPosition(anchor, NativeRotation(pair.Rotation), towerEvent.TowerIndex,
                towerEvent.SectionId % TowerLayout.SectionsPerTower);
            GAT.World.buildPartManager.RemoveBuildPart(template, position, NativeRotation(pair.Rotation));
        }
        _towerScene.Apply(pair, towerEvent);
    }

    public bool TryResolveTowerSection(int template, Vector3Int position, BuildPart.Rotation rotation,
        out TowerPair pair, out TowerSectionState section)
    {
        foreach (TowerPair candidate in World.TowerPairs)
            for (int tower = 0; tower < TowerLayout.TowerCount; tower++)
                for (int level = 0; level < TowerLayout.SectionsPerTower; level++)
                {
                    if (Blueprints.TowerSectionTemplateIndex(tower, level) != template) continue;
                    Vector3Int anchor = Vector3Int.RoundToInt(GameFlightEnvironment.ToUnity(candidate.Position));
                    if (NativeRotation(candidate.Rotation) != rotation || SectionGridPosition(anchor, rotation, tower, level) != position) continue;
                    pair = candidate;
                    section = candidate.Sections[tower * TowerLayout.SectionsPerTower + level];
                    return true;
                }
        pair = null!;
        section = null!;
        return false;
    }

    private void RenderExplosion(AircraftEvent aircraftEvent)
    {
        if (IsAuthoritative && Environment is GameFlightEnvironment environment) environment.ImpactDamage.Apply(aircraftEvent.Position);
        AircraftExplosion.Play(GameFlightEnvironment.ToUnity(aircraftEvent.Position));
    }

    private void StartGroundOrders(AircraftEvent aircraftEvent)
    {
        if (!IsAuthoritative) return;
        foreach (Occupant child in aircraftEvent.Occupants) _groundOrders.MoveChild(child, aircraftEvent.Position);
    }

    private void LandOccupants(AircraftEvent aircraftEvent)
    {
        foreach (Occupant occupant in aircraftEvent.Occupants)
        {
            Handle handle = OccupantHandle(occupant);
            if (GAT.World.gat != null && GAT.World.gat.IsHandleValid(handle))
            {
                GAT.World.gat.SetInstanceVelocity(handle, Vector3.zero);
                GAT.World.gat.SetInstanceMatrix(handle, Matrix4x4.TRS(GameFlightEnvironment.ToUnity(aircraftEvent.Position), Quaternion.identity, Vector3.one), true, true, true);
            }
            if (IsAuthoritative && occupant.Kind == OccupantKind.Child) _boarding.Release(occupant.Id);
        }
    }

    public void ClearWorld()
    {
        if (Environment is GameFlightEnvironment environment) environment.ImpactDamage.Clear();
        RemoveAllTowerSectionParts();
        _groundOrders?.Clear();
        _boarding?.Dispose();
        World.Clear();
        _towerPartsDirty = false;
        Projects?.Clear();
        _buildState?.Clear();
        _scene?.Dispose();
        _towerScene?.Dispose();
        _renderedEvents.Clear();
        _replicatedReservations.Clear();
    }

    private bool CreateTowerSectionParts(Vector3Int anchor, BuildPart.Rotation rotation)
    {
        var created = new List<(int Template, Vector3Int Position)>();
        if (!CreateTowerPart(Blueprints.TowerFoundationIndex, anchor, rotation, created)) return false;
        for (int tower = 0; tower < TowerLayout.TowerCount; tower++)
            for (int level = 0; level < TowerLayout.SectionsPerTower; level++)
            {
                int template = Blueprints.TowerSectionTemplateIndex(tower, level);
                Vector3Int position = SectionGridPosition(anchor, rotation, tower, level);
                if (!CreateTowerPart(template, position, rotation, created)) return false;
            }
        return true;
    }

    private bool CreateTowerPart(int template, Vector3Int position, BuildPart.Rotation rotation, List<(int Template, Vector3Int Position)> created)
    {
        if (GAT.World.buildPartManager.TryGetBuildPart(position, rotation, out _)) return RollBackTowerParts(created, rotation);
        GAT.World.buildPartManager.CreateBuildPart(template, position, rotation);
        if (!GAT.World.buildPartManager.TryGetBuildPart(position, rotation, out BuildPart actual) || actual != GAT.World.buildPartManager.BuildPartTemplates[template])
            return RollBackTowerParts(created, rotation);
        created.Add((template, position));
        return true;
    }

    private bool RollBackTowerParts(List<(int Template, Vector3Int Position)> created, BuildPart.Rotation rotation)
    {
        foreach ((int template, Vector3Int position) in created)
            GAT.World.buildPartManager.RemoveBuildPart(template, position, rotation);
        return false;
    }

    private void RemoveTowerSectionParts(Vector3Int anchor, BuildPart.Rotation rotation)
    {
        GAT.World.buildPartManager.RemoveBuildPart(Blueprints.TowerFoundationIndex, anchor, rotation);
        for (int tower = 0; tower < TowerLayout.TowerCount; tower++)
            for (int level = 0; level < TowerLayout.SectionsPerTower; level++)
            {
                int template = Blueprints.TowerSectionTemplateIndex(tower, level);
                Vector3Int position = SectionGridPosition(anchor, rotation, tower, level);
                GAT.World.buildPartManager.RemoveBuildPart(template, position, rotation);
            }
    }

    private void RemoveAllTowerSectionParts()
    {
        if (Blueprints == null || Blueprints.TowerSectionTemplateIndices.Count != TowerLayout.SectionsPerPair || GAT.World.buildPartManager == null) return;
        foreach (TowerPair pair in World.TowerPairs)
            RemoveTowerSectionParts(Vector3Int.RoundToInt(GameFlightEnvironment.ToUnity(pair.Position)), NativeRotation(pair.Rotation));
    }

    private void SynchronizeTowerParts()
    {
        if (GAT.World.buildPartManager == null || Blueprints.TowerSectionTemplateIndices.Count != TowerLayout.SectionsPerPair) return;
        foreach (TowerPair pair in World.TowerPairs)
        {
            Vector3Int anchor = Vector3Int.RoundToInt(GameFlightEnvironment.ToUnity(pair.Position));
            BuildPart.Rotation rotation = NativeRotation(pair.Rotation);
            EnsureTowerFoundation(anchor, rotation);
            for (int tower = 0; tower < TowerLayout.TowerCount; tower++)
                for (int level = 0; level < TowerLayout.SectionsPerTower; level++)
                {
                    int template = Blueprints.TowerSectionTemplateIndex(tower, level);
                    Vector3Int position = SectionGridPosition(anchor, rotation, tower, level);
                    if (pair.Sections[tower * TowerLayout.SectionsPerTower + level].Phase >= TowerSectionPhase.Collapsing)
                    {
                        GAT.World.buildPartManager.RemoveBuildPart(template, position, rotation);
                        continue;
                    }
                    if (!GAT.World.buildPartManager.TryGetBuildPart(position, rotation, out _))
                        GAT.World.buildPartManager.CreateBuildPart(template, position, rotation);
                }
        }
        _towerPartsDirty = false;
    }

    private void EnsureTowerFoundation(Vector3Int anchor, BuildPart.Rotation rotation)
    {
        if (GAT.World.buildPartManager.TryGetBuildPart(anchor, rotation, out _)) return;
        GAT.World.buildPartManager.CreateBuildPart(Blueprints.TowerFoundationIndex, anchor, rotation);
    }

    private static Vector3Int SectionGridPosition(Vector3Int anchor, BuildPart.Rotation rotation, int tower, int level)
    {
        int localX = tower == 0 ? -TowerLayout.HorizontalTowerOffset : TowerLayout.HorizontalTowerOffset;
        Vector3Int offset = rotation switch
        {
            BuildPart.Rotation.R_0 => new Vector3Int(localX, level * TowerLayout.SectionHeight, 0),
            BuildPart.Rotation.R_90 => new Vector3Int(0, level * TowerLayout.SectionHeight, -localX),
            BuildPart.Rotation.R_180 => new Vector3Int(-localX, level * TowerLayout.SectionHeight, 0),
            _ => new Vector3Int(0, level * TowerLayout.SectionHeight, localX)
        };
        return anchor + offset;
    }

    private static BuildPart.Rotation NativeRotation(System.Numerics.Quaternion rotation)
    {
        var forward = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(0, 0, 1), rotation);
        float degrees = Mathf.Repeat(Quaternion.LookRotation(GameFlightEnvironment.ToUnity(forward)).eulerAngles.y, 360);
        return (BuildPart.Rotation)(Mathf.RoundToInt(degrees / 90) % 4);
    }

    private void OnDestroy()
    {
        ClearWorld();
        _models?.Dispose();
        if (Current == this) Current = null;
    }
}
