using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace FamilyTimeAircraft.Core;

public sealed partial class AircraftWorld
{
    internal readonly Dictionary<int, Aircraft> Planes = new Dictionary<int, Aircraft>();
    internal readonly Dictionary<int, Airfield> Runways = new Dictionary<int, Airfield>();
    internal readonly Dictionary<string, EjectedOccupant> FlyingOccupants = new Dictionary<string, EjectedOccupant>();
    private readonly List<AircraftEvent> _events = new List<AircraftEvent>();
    internal long EventSequence;
    internal int NextAircraftId = 1;
    internal int NextAirfieldId = 1;
    public IReadOnlyCollection<Aircraft> Aircrafts => Planes.Values;
    public IReadOnlyCollection<Airfield> Airfields => Runways.Values;
    public IReadOnlyCollection<EjectedOccupant> Ejections => FlyingOccupants.Values;
    public Aircraft FindAircraft(int id) => Planes.TryGetValue(id, out Aircraft plane) ? plane : throw new InvalidOperationException("Unknown aircraft.");
    public Airfield FindAirfield(int id) => Runways.TryGetValue(id, out Airfield field) ? field : throw new InvalidOperationException("Unknown airfield.");

    public Aircraft BuildAircraft(string owner, Vector3 position, Quaternion rotation)
    {
        if (Planes.Count >= AircraftRules.MaximumAircraft) throw new InvalidOperationException("Aircraft limit reached.");
        var aircraft = new Aircraft(NextAircraftId, owner, position, rotation);
        Planes.Add(aircraft.Id, aircraft); NextAircraftId++;
        return aircraft;
    }

    public Airfield BuildAirfield(string owner, Vector3 position, Quaternion rotation)
    {
        if (Runways.Count >= AircraftRules.MaximumAirfields) throw new InvalidOperationException("Airfield limit reached.");
        var airfield = new Airfield(NextAirfieldId, owner, position, rotation);
        Runways.Add(airfield.Id, airfield); NextAirfieldId++;
        return airfield;
    }

    public int Board(int aircraftId, Occupant occupant, int seatIndex = -1)
    {
        Aircraft plane = FindAircraft(aircraftId);
        if (!plane.CanBoard) throw new InvalidOperationException("Boarding requires a stopped aircraft on the ground.");
        if (seatIndex == AircraftRules.AutomaticBoardingSeat)
            seatIndex = AircraftRules.ResolveAutomaticBoardingSeat(plane, occupant.Owner);
        if (occupant.Kind == OccupantKind.Child || seatIndex == 0) AircraftRules.RequireOwner(plane, occupant.Owner);
        if (IsOccupied(occupant)) throw new InvalidOperationException("This passenger is already aboard or being ejected.");
        int seat = seatIndex < 0 ? Enumerable.Range(1, AircraftRules.PassengerCapacity).FirstOrDefault(index => !plane.Cabin.ContainsKey(index)) : seatIndex;
        if (seat < 0 || seat > AircraftRules.PassengerCapacity || (seatIndex < 0 && seat == 0) || plane.Cabin.ContainsKey(seat)) throw new InvalidOperationException("No available seat.");
        plane.Cabin.Add(seat, occupant);
        return seat;
    }

    public Occupant Disembark(int aircraftId, OccupantKind kind, int occupantId, IFlightEnvironment? environment = null)
    {
        Aircraft plane = FindAircraft(aircraftId);
        if (!plane.CanBoard)
            throw new InvalidOperationException("Wait until the aircraft has stopped on the ground.");
        var seat = plane.Cabin.FirstOrDefault(pair => pair.Value.Kind == kind && pair.Value.Id == occupantId);
        if (seat.Value == null) throw new InvalidOperationException("This passenger is not aboard.");
        Vector3 exit = FindExit(plane, environment);
        plane.Cabin.Remove(seat.Key);
        if (seat.Key == 0) { plane.Input = default; plane.Phase = FlightPhase.Parked; }
        _events.Add(new AircraftEvent(AircraftEventKind.OccupantLanded, ++EventSequence, plane.Id, exit, new[] { seat.Value }));
        return seat.Value;
    }

    public void ExitPlayer(int aircraftId, int playerId, IFlightEnvironment environment)
    {
        Aircraft plane = FindAircraft(aircraftId);
        var seat = plane.Cabin.FirstOrDefault(item => item.Value.Kind == OccupantKind.Player && item.Value.Id == playerId);
        if (seat.Value == null) throw new InvalidOperationException("This player is not aboard.");
        if (plane.CanBoard) { Disembark(aircraftId, OccupantKind.Player, playerId, environment); return; }
        Vector3 target = FindParachuteLanding(plane.Position, environment);
        LaunchParachute(plane, seat.Value, target, environment);
        plane.Cabin.Remove(seat.Key);
        if (seat.Key == 0) AbandonPilotControls(plane);
    }

    private void AbandonPilotControls(Aircraft plane)
    {
        ReleaseRunways(plane); DropPlans.Remove(plane.Id); ClearPilotGroundOrder(plane);
        plane.Input = default; plane.CrashTarget = null; plane.TargetAirfieldId = null; plane.EmergencyLandingPoint = null;
        plane.Phase = FlightPhase.Unpiloted;
    }

    private static Vector3 FindExit(Aircraft plane, IFlightEnvironment? environment)
    {
        if (TryFindExit(plane, environment, out Vector3 exit)) return exit;
        throw new InvalidOperationException("There is no safe place to disembark.");
    }

    private static bool TryFindExit(Aircraft plane, IFlightEnvironment? environment, out Vector3 result)
    {
        foreach (Vector3 direction in new[] { Vector3.UnitX, -Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitZ })
        {
            Vector3 exit = plane.Position + Vector3.Transform(direction * 7, plane.Rotation);
            if (environment == null) { result = exit; return true; }
            exit.Y = environment.GroundHeight(exit);
            if (environment.IsInWorld(exit) && !environment.IsBlocked(exit + Vector3.UnitY * 0.3f, exit + Vector3.UnitY * 2, 0.3f)) { result = exit; return true; }
        }
        result = default; return false;
    }

    public void ApplyAuthoritativeLanding(Occupant occupant)
    {
        foreach (Aircraft plane in Planes.Values)
            foreach (int seat in plane.Cabin.Where(item => item.Value.Key == occupant.Key).Select(item => item.Key).ToArray()) plane.Cabin.Remove(seat);
        FlyingOccupants.Remove(occupant.Key);
    }

    public void RecoverUncontrolledAircraft(int aircraftId, IFlightEnvironment environment) => ReturnUnpilotedAircraft(FindAircraft(aircraftId), environment);

    public void SetManualControl(int aircraftId, int playerId, FlightInput input)
    {
        Aircraft plane = FindAircraft(aircraftId);
        if (!plane.Cabin.TryGetValue(0, out Occupant pilot) || pilot.Kind != OccupantKind.Player || pilot.Id != playerId || plane.Phase == FlightPhase.Wrecked)
            throw new InvalidOperationException("Only the seated player pilot can control this aircraft.");
        ReleaseRunways(plane); plane.CrashTarget = null; plane.TargetAirfieldId = null;
        plane.Input = input; plane.Phase = FlightPhase.Manual;
    }

    public void OrderFlight(int aircraftId, int targetAirfieldId, string issuerOwner, IFlightEnvironment environment)
    {
        Aircraft plane = RequireChildPilot(aircraftId, issuerOwner);
        Airfield target = FindAirfield(targetAirfieldId);
        if (!environment.IsRunwayClear(target) || target.ReservedBy.HasValue && target.ReservedBy != plane.Id)
            throw new InvalidOperationException("The destination runway is blocked or occupied.");
        Airfield? origin = PrepareDeparture(plane, environment);
        if (origin?.Id == target.Id) throw new InvalidOperationException("Choose another destination airfield.");
        ReleaseRunways(plane); ReserveDeparture(plane, origin); target.ReservedBy = plane.Id;
        plane.TargetAirfieldId = target.Id; plane.CrashTarget = null;
    }

    public void OrderCrash(int aircraftId, Vector3 target, string issuerOwner, IFlightEnvironment environment)
    {
        Aircraft plane = RequireChildPilot(aircraftId, issuerOwner);
        if (!environment.IsInWorld(target)) throw new InvalidOperationException("The crash target is outside the world.");
        Airfield? origin = PrepareDeparture(plane, environment);
        ReleaseRunways(plane); ReserveDeparture(plane, origin);
        ClearPilotGroundOrder(plane);
        plane.CrashTarget = target; plane.TargetAirfieldId = null;
        if (origin == null) plane.Phase = FlightPhase.CrashApproach;
    }

    public void Repair(int aircraftId, string issuerOwner)
    {
        Aircraft plane = FindAircraft(aircraftId);
        AircraftRules.RequireOwner(plane, issuerOwner);
        if (plane.Phase != FlightPhase.Wrecked) throw new InvalidOperationException("Only a wreck needs repair.");
        plane.Phase = FlightPhase.Parked; plane.Velocity = Vector3.Zero;
        plane.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, plane.Yaw);
        plane.Pitch = 0; plane.Roll = 0;
    }

    public void PlayerDisconnected(int playerId, IFlightEnvironment environment)
    {
        foreach (Aircraft plane in Planes.Values)
        {
            bool pilotLeft = plane.Cabin.TryGetValue(0, out Occupant pilot) && pilot.Kind == OccupantKind.Player && pilot.Id == playerId;
            foreach (int seat in plane.Cabin.Where(item => item.Value.Kind == OccupantKind.Player && item.Value.Id == playerId).Select(item => item.Key).ToArray()) plane.Cabin.Remove(seat);
            if (pilotLeft && plane.Phase != FlightPhase.Parked && plane.Phase != FlightPhase.Wrecked) ReturnUnpilotedAircraft(plane, environment);
        }
        FlyingOccupants.Remove($"{(byte)OccupantKind.Player}:{playerId}");
    }

    public void ReleaseRemotePlayers(int localPlayerId, IFlightEnvironment environment)
    {
        int[] remotePlayers = Planes.Values.SelectMany(plane => plane.Cabin.Values)
            .Concat(FlyingOccupants.Values.Select(passenger => passenger.Occupant))
            .Where(occupant => occupant.Kind == OccupantKind.Player && occupant.Id != localPlayerId)
            .Select(occupant => occupant.Id).Distinct().ToArray();
        foreach (int playerId in remotePlayers) PlayerDisconnected(playerId, environment);
    }

    private void ReturnUnpilotedAircraft(Aircraft plane, IFlightEnvironment environment)
    {
        ReleaseRunways(plane); plane.Input = default; plane.CrashTarget = null;
        Airfield? destination = Runways.Values.Where(field => !field.ReservedBy.HasValue && environment.IsRunwayClear(field))
            .OrderBy(field => Vector3.DistanceSquared(plane.Position, field.Position)).FirstOrDefault();
        plane.TargetAirfieldId = destination?.Id;
        if (destination != null) { destination.ReservedBy = plane.Id; plane.Phase = FlightPhase.Cruising; }
        else
        {
            plane.EmergencyLandingPoint = new Vector3(plane.Position.X, environment.GroundHeight(plane.Position), plane.Position.Z);
            plane.Phase = FlightPhase.EmergencyLanding;
        }
    }

    public void Tick(float seconds, IFlightEnvironment environment)
    {
        if (!AircraftRules.IsFinite(seconds) || seconds < 0 || seconds > 1) throw new ArgumentOutOfRangeException(nameof(seconds));
        while (seconds > 0)
        {
            float step = Math.Min(seconds, AircraftRules.FixedStep);
            foreach (Aircraft plane in Planes.Values) AircraftMotion.Step(this, plane, step, environment);
            StepParachuteDrops(step, environment);
            StepEjections(step, environment);
            seconds -= step;
        }
    }

    public IReadOnlyList<AircraftEvent> DrainEvents()
    {
        AircraftEvent[] events = _events.ToArray(); _events.Clear(); return events;
    }

    public void Clear()
    {
        Planes.Clear(); Runways.Clear(); FlyingOccupants.Clear(); DropPlans.Clear(); _events.Clear();
        ClearTowers();
        NextAircraftId = 1; NextAirfieldId = 1; EventSequence = 0;
    }

    internal void Explode(Aircraft plane, IFlightEnvironment environment)
    {
        if (plane.Phase == FlightPhase.Wrecked) return;
        Occupant[] passengers = plane.Cabin.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray();
        for (int i = 0; i < passengers.Length; i++) Eject(plane, passengers[i], i, passengers.Length, environment);
        plane.Cabin.Clear(); ReleaseRunways(plane); DropPlans.Remove(plane.Id); ClearPilotGroundOrder(plane);
        plane.Phase = FlightPhase.Wrecked; plane.Velocity = Vector3.Zero; plane.Input = default;
        plane.CrashTarget = null; plane.TargetAirfieldId = null;
        _events.Add(new AircraftEvent(AircraftEventKind.Explosion, ++EventSequence, plane.Id, plane.Position, passengers));
    }

    internal void ReleaseRunways(Aircraft plane)
    {
        foreach (Airfield field in Runways.Values) if (field.ReservedBy == plane.Id) field.ReservedBy = null;
        plane.OriginAirfieldId = null;
    }

    private bool IsOccupied(Occupant occupant) => FlyingOccupants.ContainsKey(occupant.Key) || Planes.Values.Any(plane => plane.Cabin.Values.Any(existing => existing.Key == occupant.Key));

    private Aircraft RequireChildPilot(int aircraftId, string owner)
    {
        Aircraft plane = FindAircraft(aircraftId); AircraftRules.RequireOwner(plane, owner);
        if (!plane.Cabin.TryGetValue(0, out Occupant pilot) || pilot.Kind != OccupantKind.Child || plane.Phase == FlightPhase.Wrecked)
            throw new InvalidOperationException("Assign a child pilot before issuing this order.");
        return plane;
    }

    private Airfield? PrepareDeparture(Aircraft plane, IFlightEnvironment environment)
    {
        if (plane.Phase != FlightPhase.Parked) return null;
        Airfield? origin = Runways.Values.FirstOrDefault(field => Vector3.Distance(field.Position, plane.Position) <= Airfield.Width);
        if (origin == null || !environment.IsRunwayClear(origin) || origin.ReservedBy.HasValue && origin.ReservedBy != plane.Id)
            throw new InvalidOperationException("A clear departure airfield is required.");
        return origin;
    }

    private static void ReserveDeparture(Aircraft plane, Airfield? origin)
    {
        if (origin != null) { origin.ReservedBy = plane.Id; plane.OriginAirfieldId = origin.Id; plane.Phase = FlightPhase.Taxiing; }
        else plane.Phase = FlightPhase.Cruising;
    }

    private void Eject(Aircraft plane, Occupant occupant, int index, int count, IFlightEnvironment environment)
    {
        float angle = (index + 0.5f) * MathF.PI * 2 / Math.Max(1, count);
        var velocity = new Vector3(MathF.Sin(angle) * 10, 11 + index % 3, MathF.Cos(angle) * 10);
        FlyingOccupants.Add(occupant.Key, new EjectedOccupant(occupant, plane.Id, FindOccupantReleasePosition(plane, environment), velocity));
    }

    private void StepEjections(float seconds, IFlightEnvironment environment)
    {
        foreach (EjectedOccupant passenger in FlyingOccupants.Values.ToArray())
        {
            passenger.Elapsed += seconds;
            if (passenger.HasParachute) { StepParachutist(passenger, seconds, environment); continue; }
            passenger.Velocity -= Vector3.UnitY * AircraftRules.Gravity * seconds;
            Vector3 next = passenger.Position + passenger.Velocity * seconds;
            bool blocked = environment.IsBlocked(passenger.Position, next, 0.3f);
            if (environment.IsInWorld(next) && !blocked) passenger.Position = next;
            float ground = environment.GroundHeight(passenger.Position);
            if (passenger.Position.Y <= ground || blocked || passenger.Elapsed >= AircraftRules.EjectionTimeout) LandPassenger(passenger, ground);
        }
    }

    private void LandPassenger(EjectedOccupant passenger, float height)
    {
        passenger.Position = new Vector3(passenger.Position.X, height, passenger.Position.Z);
        FlyingOccupants.Remove(passenger.Occupant.Key);
        _events.Add(new AircraftEvent(AircraftEventKind.OccupantLanded, ++EventSequence, passenger.AircraftId, passenger.Position, new[] { passenger.Occupant }));
        if (passenger.Occupant.Kind == OccupantKind.Child && passenger.ParachuteTarget.HasValue)
            _events.Add(new AircraftEvent(AircraftEventKind.GroundGoTo, ++EventSequence, passenger.AircraftId, passenger.ParachuteTarget.Value, new[] { passenger.Occupant }));
    }
}
