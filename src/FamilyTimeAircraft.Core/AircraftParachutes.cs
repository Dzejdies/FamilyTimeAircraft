using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace FamilyTimeAircraft.Core;

internal sealed class PendingParachuteDrop
{
    internal PendingParachuteDrop(int aircraftId, string owner, Vector3 origin, List<ParachuteDropPassenger> passengers)
    {
        AircraftId = aircraftId; Owner = AircraftRules.RequireOwner(owner); Origin = origin; Passengers = passengers;
    }
    internal int AircraftId { get; }
    internal string Owner { get; }
    internal Vector3 Origin { get; }
    internal List<ParachuteDropPassenger> Passengers { get; }
    internal float UntilNext;
}

internal readonly struct ParachuteDropPassenger
{
    internal ParachuteDropPassenger(int childId, Vector3 target) { ChildId = childId; Target = target; }
    internal int ChildId { get; }
    internal Vector3 Target { get; }
}

public sealed partial class AircraftWorld
{
    internal readonly Dictionary<int, PendingParachuteDrop> DropPlans = new Dictionary<int, PendingParachuteDrop>();
    public int PendingParachuteDrops => DropPlans.Count;

    public IReadOnlyList<Occupant> AircraftChildrenForGoTo(int aircraftId, string issuerOwner)
    {
        Aircraft plane = FindAircraft(aircraftId);
        AircraftRules.RequireOwner(plane, issuerOwner);
        return Array.AsReadOnly(plane.Cabin.Values.Where(occupant => occupant.Kind == OccupantKind.Child && occupant.Owner == issuerOwner).ToArray());
    }

    public IReadOnlyList<Occupant> ParachutePassengers(int aircraftId, string issuerOwner)
    {
        Aircraft plane = FindAircraft(aircraftId);
        AircraftRules.RequireOwner(plane, issuerOwner);
        return Array.AsReadOnly(plane.Cabin.Where(seat => seat.Key > 0 && seat.Value.Kind == OccupantKind.Child && seat.Value.Owner == issuerOwner).Select(seat => seat.Value).ToArray());
    }

    public void OrderParachuteDrop(int aircraftId, Vector3 target, string issuerOwner, IFlightEnvironment environment)
    {
        Aircraft plane = FindAircraft(aircraftId);
        IReadOnlyList<Occupant> children = AircraftChildrenForGoTo(aircraftId, issuerOwner);
        if (children.Count == 0) throw new InvalidOperationException("There are no children aboard for this order.");
        Vector3 landing = RequireLandingTarget(target, environment);
        if (plane.CanBoard) { UnloadChildrenForGoTo(plane, children, landing, environment); return; }
        if (DropPlans.ContainsKey(aircraftId)) throw new InvalidOperationException("A parachute drop is already in progress.");
        IReadOnlyList<Occupant> passengers = ParachutePassengers(aircraftId, issuerOwner);
        List<ParachuteDropPassenger> drops = PreparePassengerDrops(plane, passengers, landing, environment);
        PreparePilotReturn(plane, issuerOwner, landing, environment);
        if (drops.Count > 0) DropPlans.Add(plane.Id, new PendingParachuteDrop(plane.Id, issuerOwner, plane.Position, drops));
        else BeginPilotReturn(plane, environment);
    }

    private static Vector3 RequireLandingTarget(Vector3 target, IFlightEnvironment environment)
    {
        AircraftRules.RequirePosition(target);
        if (!environment.IsInWorld(target)) throw new InvalidOperationException("The landing point is outside the world.");
        target.Y = environment.GroundHeight(target);
        if (!environment.IsInWorld(target) || environment.IsBlocked(target + Vector3.UnitY * 0.3f, target + Vector3.UnitY * 2, 0.3f))
            throw new InvalidOperationException("The landing point is blocked.");
        return target;
    }

    private static Vector3 FindParachuteLanding(Vector3 origin, IFlightEnvironment environment)
    {
        foreach (float radius in new[] { 0f, 8f, 16f, 32f })
            foreach (Vector3 direction in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ })
            {
                Vector3 target = origin + direction * radius;
                target.Y = environment.GroundHeight(target);
                if (environment.IsInWorld(target) && !environment.IsBlocked(target + Vector3.UnitY * 0.3f, target + Vector3.UnitY * 2, 0.3f)) return target;
            }
        throw new InvalidOperationException("There is no clear nearby parachute landing point.");
    }

    private static void RequireGlideRange(Vector3 origin, Vector3 target)
    {
        float height = origin.Y - target.Y;
        float range = Math.Min(AircraftRules.ParachuteMaximumRange, height * AircraftRules.ParachuteGlideRatio);
        Vector3 delta = target - origin; delta.Y = 0;
        if (height < AircraftRules.ParachuteMinimumHeight || delta.Length() > range)
            throw new InvalidOperationException("Fly closer to the landing point or higher before dropping children.");
    }

    private static List<ParachuteDropPassenger> PreparePassengerDrops(Aircraft plane, IReadOnlyList<Occupant> passengers, Vector3 target, IFlightEnvironment environment)
    {
        if (plane.Phase == FlightPhase.Wrecked || plane.IsOnGround || !plane.Cabin.ContainsKey(0)) throw new InvalidOperationException("An airborne aircraft with a pilot is required.");
        var drops = new List<ParachuteDropPassenger>();
        for (int index = 0; index < passengers.Count; index++)
        {
            Vector3 offset = new((index % 3 - 1) * AircraftRules.ParachuteLandingSpread, 0, (index / 3 - 1) * AircraftRules.ParachuteLandingSpread);
            Vector3 landing = RequireLandingTarget(target + offset, environment);
            Vector3 releaseOrigin = plane.Position + plane.Velocity * (index * AircraftRules.ParachuteDropInterval);
            RequireGlideRange(releaseOrigin, landing);
            drops.Add(new ParachuteDropPassenger(passengers[index].Id, landing));
        }
        return drops;
    }

    private void PreparePilotReturn(Aircraft plane, string owner, Vector3 target, IFlightEnvironment environment)
    {
        if (plane.CrashTarget.HasValue || !plane.Cabin.TryGetValue(0, out Occupant pilot) || pilot.Kind != OccupantKind.Child || pilot.Owner != owner) return;
        if (NearestClearAirfield(plane, environment) == null) throw new InvalidOperationException("No clear airfield is available for the pilot to return.");
        plane.PilotGroundTarget = target; plane.PilotGroundOwner = owner; plane.PilotReturnPending = true;
    }

    private Airfield? NearestClearAirfield(Aircraft plane, IFlightEnvironment environment) => Runways.Values
        .Where(field => (!field.ReservedBy.HasValue || field.ReservedBy == plane.Id) && environment.IsRunwayClear(field))
        .OrderBy(field => Vector3.DistanceSquared(plane.Position, field.Position)).FirstOrDefault();

    private void BeginPilotReturn(Aircraft plane, IFlightEnvironment environment)
    {
        if (!plane.PilotReturnPending || plane.CrashTarget.HasValue || DropPlans.ContainsKey(plane.Id)) return;
        Airfield? destination = NearestClearAirfield(plane, environment);
        if (destination == null) return;
        OrderFlight(plane.Id, destination.Id, plane.PilotGroundOwner!, environment);
        plane.PilotReturnPending = false;
    }

    private void UnloadChildrenForGoTo(Aircraft plane, IReadOnlyList<Occupant> children, Vector3 target, IFlightEnvironment environment)
    {
        FindExit(plane, environment);
        DropPlans.Remove(plane.Id); ClearPilotGroundOrder(plane);
        foreach (Occupant child in children)
        {
            Disembark(plane.Id, OccupantKind.Child, child.Id, environment);
            QueueGroundGoTo(plane.Id, child, target);
        }
    }

    private void QueueGroundGoTo(int aircraftId, Occupant child, Vector3 target) =>
        _events.Add(new AircraftEvent(AircraftEventKind.GroundGoTo, ++EventSequence, aircraftId, target, new[] { child }));

    internal void LaunchParachute(Aircraft plane, Occupant occupant, Vector3 landingTarget, IFlightEnvironment environment)
    {
        Vector3 position = FindOccupantReleasePosition(plane, environment);
        var parachutist = new EjectedOccupant(occupant, plane.Id, position, -Vector3.UnitY * AircraftRules.ParachuteSinkSpeed);
        parachutist.ParachuteTarget = landingTarget;
        FlyingOccupants.Add(occupant.Key, parachutist);
    }

    private static Vector3 FindOccupantReleasePosition(Aircraft plane, IFlightEnvironment environment)
    {
        Vector3 position = plane.Position + Vector3.UnitY * AircraftRules.OccupantReleaseHeight;
        if (!environment.IsInWorld(position)) position = plane.Position;
        if (!environment.IsInWorld(position)) throw new InvalidOperationException("The aircraft is outside occupant release bounds.");
        return position;
    }

    private void StepParachuteDrops(float seconds, IFlightEnvironment environment)
    {
        foreach (PendingParachuteDrop plan in DropPlans.Values.ToArray())
        {
            plan.UntilNext -= seconds;
            if (plan.UntilNext > 0) continue;
            if (!TryReleaseParachutist(plan, environment)) DropPlans.Remove(plan.AircraftId);
            else if (plan.Passengers.Count == 0) DropPlans.Remove(plan.AircraftId);
            else plan.UntilNext += AircraftRules.ParachuteDropInterval;
        }
        foreach (Aircraft plane in Planes.Values.Where(plane => plane.PilotGroundTarget.HasValue).ToArray()) StepPilotGroundOrder(plane, environment);
    }

    private bool TryReleaseParachutist(PendingParachuteDrop plan, IFlightEnvironment environment)
    {
        Aircraft plane = FindAircraft(plan.AircraftId);
        ParachuteDropPassenger next = plan.Passengers[0];
        var seat = plane.Cabin.FirstOrDefault(item => item.Key > 0 && item.Value.Kind == OccupantKind.Child && item.Value.Id == next.ChildId && item.Value.Owner == plan.Owner);
        if (seat.Value == null || plane.IsOnGround || !plane.Cabin.ContainsKey(0) || plane.Phase == FlightPhase.Wrecked) return false;
        try { RequireLandingTarget(next.Target, environment); }
        catch (InvalidOperationException) { return false; }
        LaunchParachute(plane, seat.Value, next.Target, environment);
        plane.Cabin.Remove(seat.Key); plan.Passengers.RemoveAt(0);
        return true;
    }

    private void StepPilotGroundOrder(Aircraft plane, IFlightEnvironment environment)
    {
        if (plane.CrashTarget.HasValue) { ClearPilotGroundOrder(plane); return; }
        if (plane.PilotReturnPending) { BeginPilotReturn(plane, environment); return; }
        if (!plane.CanBoard || !plane.Cabin.TryGetValue(0, out Occupant pilot) || pilot.Kind != OccupantKind.Child || pilot.Owner != plane.PilotGroundOwner) return;
        if (!TryFindExit(plane, environment, out _)) return;
        Vector3 target = plane.PilotGroundTarget!.Value;
        Disembark(plane.Id, OccupantKind.Child, pilot.Id, environment);
        ClearPilotGroundOrder(plane);
        QueueGroundGoTo(plane.Id, pilot, target);
    }

    private static void ClearPilotGroundOrder(Aircraft plane)
    {
        plane.PilotGroundTarget = null; plane.PilotGroundOwner = null; plane.PilotReturnPending = false;
    }

    private static bool CanLandParachutist(Vector3 position, float ground, IFlightEnvironment environment)
    {
        position.Y = ground;
        return environment.IsInWorld(position) && !environment.IsBlocked(position + Vector3.UnitY * AircraftRules.OccupantCollisionRadius,
            position + Vector3.UnitY * AircraftRules.OccupantReleaseHeight, AircraftRules.OccupantCollisionRadius);
    }

    private void StepParachutist(EjectedOccupant passenger, float seconds, IFlightEnvironment environment)
    {
        passenger.Elapsed = Math.Min(passenger.Elapsed, AircraftRules.ParachuteMaximumElapsed);
        Vector3 target = passenger.ParachuteTarget!.Value;
        Vector3 delta = target - passenger.Position; delta.Y = 0;
        float time = Math.Max(seconds, (passenger.Position.Y - target.Y) / AircraftRules.ParachuteSinkSpeed);
        Vector3 horizontal = delta / time;
        if (horizontal.Length() > AircraftRules.ParachuteMaximumHorizontalSpeed) horizontal = Vector3.Normalize(horizontal) * AircraftRules.ParachuteMaximumHorizontalSpeed;
        passenger.Velocity = horizontal - Vector3.UnitY * AircraftRules.ParachuteSinkSpeed;
        Vector3 next = passenger.Position + passenger.Velocity * seconds;
        float nextGround = environment.GroundHeight(next);
        if (next.Y <= nextGround + AircraftRules.OccupantCollisionRadius && CanLandParachutist(next, nextGround, environment))
        {
            passenger.Position = next;
            LandPassenger(passenger, nextGround);
            return;
        }
        if (environment.IsInWorld(next) && !environment.IsBlocked(passenger.Position, next, AircraftRules.OccupantCollisionRadius)) passenger.Position = next;
        float ground = environment.GroundHeight(passenger.Position);
        if (passenger.Position.Y <= ground && CanLandParachutist(passenger.Position, ground, environment)) LandPassenger(passenger, ground);
    }
}
