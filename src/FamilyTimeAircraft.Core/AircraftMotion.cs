using System;
using System.Numerics;

namespace FamilyTimeAircraft.Core;

internal static class AircraftMotion
{
    private const float LandingImpactSpeed = 5;
    private const float TaxiSpeed = 7;
    private const float WaypointTolerance = 0.5f;
    private const float TerrainSampleSpacing = 8;
    private const float AltitudeTolerance = 0.1f;

    public static void Step(AircraftWorld world, Aircraft plane, float seconds, IFlightEnvironment environment)
    {
        if (plane.Phase == FlightPhase.Parked || plane.Phase == FlightPhase.Wrecked) return;
        if (plane.Phase == FlightPhase.Unpiloted) StepUnpiloted(world, plane, seconds, environment);
        else if (plane.Phase == FlightPhase.Manual) StepManual(world, plane, seconds, environment);
        else StepAutopilot(world, plane, seconds, environment);
    }

    private static void StepUnpiloted(AircraftWorld world, Aircraft plane, float seconds, IFlightEnvironment environment)
    {
        plane.Velocity = LimitSpeed(plane.Velocity - Vector3.UnitY * AircraftRules.Gravity * seconds);
        CommitMove(world, plane, plane.Position + plane.Velocity * seconds, environment, true);
    }

    private static void StepManual(AircraftWorld world, Aircraft plane, float seconds, IFlightEnvironment environment)
    {
        UpdateRotation(plane, seconds);
        float speed = plane.Velocity.Length();
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, plane.Rotation);
        float lift = AircraftRules.Gravity * Math.Min(1.6f, speed * speed / (18 * 18));
        Vector3 liftDirection = Vector3.Transform(Vector3.UnitY, plane.Rotation);
        float engineThrottle = plane.Input.Brake ? 0 : plane.Input.Throttle;
        Vector3 acceleration = forward * (engineThrottle * 12) + liftDirection * lift - Vector3.UnitY * AircraftRules.Gravity
            - plane.Velocity * (plane.Input.Brake ? 1.6f : 0.18f);
        plane.Velocity = LimitSpeed(plane.Velocity + acceleration * seconds);
        CommitMove(world, plane, plane.Position + plane.Velocity * seconds, environment, plane.Velocity.Y < -LandingImpactSpeed);
    }

    private static void UpdateRotation(Aircraft plane, float seconds)
    {
        plane.Yaw += (plane.Input.Yaw * 0.9f + plane.Input.Roll * 0.25f) * seconds;
        plane.Pitch = Approach(plane.Pitch, plane.Input.Pitch * 0.6f, seconds * 0.9f);
        plane.Roll = Approach(plane.Roll, -plane.Input.Roll * 0.7f, seconds);
        plane.Rotation = Quaternion.CreateFromYawPitchRoll(plane.Yaw, -plane.Pitch, plane.Roll);
    }

    private static void StepAutopilot(AircraftWorld world, Aircraft plane, float seconds, IFlightEnvironment environment)
    {
        Vector3 target = Target(world, plane, environment);
        float distance = Vector3.Distance(plane.Position, target);
        float speed = plane.Phase == FlightPhase.Taxiing || plane.Phase == FlightPhase.Landing || plane.Phase == FlightPhase.EmergencyLanding ? TaxiSpeed : AircraftRules.CruiseSpeed;
        Vector3 next = distance <= speed * seconds ? target : plane.Position + (target - plane.Position) * (speed * seconds / distance);
        plane.Velocity = (next - plane.Position) / seconds;
        FaceMovement(plane, seconds);
        bool crash = plane.Phase == FlightPhase.CrashApproach;
        CommitMove(world, plane, next, environment, crash);
        if (plane.Phase != FlightPhase.Wrecked && Vector3.Distance(plane.Position, target) <= WaypointTolerance && CanAdvance(world, plane, environment)) AdvancePhase(world, plane, environment);
    }

    private static Vector3 Target(AircraftWorld world, Aircraft plane, IFlightEnvironment environment)
    {
        if (plane.Phase == FlightPhase.CrashApproach) return SafeCrashTarget(plane, environment);
        if (plane.Phase == FlightPhase.EmergencyLanding) return plane.EmergencyLandingPoint ?? throw new InvalidOperationException("An emergency landing has no target.");
        if (plane.Phase == FlightPhase.Taxiing || plane.Phase == FlightPhase.TakingOff)
        {
            Airfield origin = world.FindAirfield(plane.OriginAirfieldId ?? throw new InvalidOperationException("Missing departure runway."));
            return plane.Phase == FlightPhase.Taxiing ? origin.Entry : origin.Exit + Vector3.UnitY * AircraftRules.CruiseAltitude;
        }
        Airfield field = world.FindAirfield(plane.TargetAirfieldId ?? throw new InvalidOperationException("Missing destination runway."));
        if (!environment.IsRunwayClear(field)) plane.Phase = FlightPhase.Cruising;
        return plane.Phase == FlightPhase.Cruising ? SafeCruiseTarget(plane, field.Entry, environment)
            : plane.Phase == FlightPhase.Approaching ? field.Entry : field.Position;
    }

    private static Vector3 SafeCruiseTarget(Aircraft plane, Vector3 destination, IFlightEnvironment environment)
    {
        float altitude = Math.Max(plane.Position.Y, destination.Y + AircraftRules.CruiseAltitude);
        int samples = Math.Max(1, (int)Math.Ceiling(Vector3.Distance(plane.Position, destination) / TerrainSampleSpacing));
        for (int sample = 0; sample <= samples; sample++)
            altitude = Math.Max(altitude, environment.GroundHeight(Vector3.Lerp(plane.Position, destination, (float)sample / samples)) + AircraftRules.CruiseAltitude);
        Vector3 target = plane.Position.Y < altitude - AltitudeTolerance ? plane.Position : destination;
        return new Vector3(target.X, altitude, target.Z);
    }

    private static Vector3 SafeCrashTarget(Aircraft plane, IFlightEnvironment environment)
    {
        Vector3 target = plane.CrashTarget ?? throw new InvalidOperationException("A crash approach has no target.");
        Vector3 horizontalDelta = plane.Position - target;
        horizontalDelta.Y = 0;
        return horizontalDelta.Length() <= WaypointTolerance ? target : SafeCruiseTarget(plane, target, environment);
    }

    private static bool CanAdvance(AircraftWorld world, Aircraft plane, IFlightEnvironment environment)
    {
        if (plane.Phase == FlightPhase.CrashApproach)
            return Vector3.Distance(plane.Position, plane.CrashTarget ?? throw new InvalidOperationException("A crash approach has no target.")) <= WaypointTolerance;
        if (plane.Phase != FlightPhase.Cruising) return true;
        Airfield destination = world.FindAirfield(plane.TargetAirfieldId ?? throw new InvalidOperationException("Missing destination runway."));
        Vector3 horizontalDelta = plane.Position - destination.Entry;
        horizontalDelta.Y = 0;
        return horizontalDelta.Length() <= WaypointTolerance && environment.IsRunwayClear(destination);
    }

    private static void AdvancePhase(AircraftWorld world, Aircraft plane, IFlightEnvironment environment)
    {
        switch (plane.Phase)
        {
            case FlightPhase.Taxiing: plane.Phase = FlightPhase.TakingOff; break;
            case FlightPhase.TakingOff:
                if (plane.OriginAirfieldId.HasValue) world.FindAirfield(plane.OriginAirfieldId.Value).ReservedBy = null;
                plane.OriginAirfieldId = null;
                plane.Phase = plane.CrashTarget.HasValue ? FlightPhase.CrashApproach : FlightPhase.Cruising; break;
            case FlightPhase.Cruising: plane.Phase = FlightPhase.Approaching; break;
            case FlightPhase.Approaching: plane.Phase = FlightPhase.Landing; break;
            case FlightPhase.Landing: world.ReleaseRunways(plane); Park(plane); break;
            case FlightPhase.EmergencyLanding: plane.Position = plane.EmergencyLandingPoint!.Value; Park(plane); plane.EmergencyLandingPoint = null; break;
            case FlightPhase.CrashApproach: world.Explode(plane, environment); break;
        }
    }

    private static void Park(Aircraft plane)
    {
        plane.Phase = FlightPhase.Parked;
        plane.Velocity = Vector3.Zero;
        plane.Pitch = 0;
        plane.Roll = 0;
        plane.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, plane.Yaw);
        plane.IsOnGround = true;
    }

    private static void FaceMovement(Aircraft plane, float seconds)
    {
        if (plane.Velocity.LengthSquared() < 0.001f) return;
        plane.Yaw = MathF.Atan2(plane.Velocity.X, plane.Velocity.Z);
        float pitch = -MathF.Atan2(plane.Velocity.Y, MathF.Sqrt(plane.Velocity.X * plane.Velocity.X + plane.Velocity.Z * plane.Velocity.Z));
        plane.Rotation = Quaternion.Slerp(plane.Rotation, Quaternion.CreateFromYawPitchRoll(plane.Yaw, pitch, 0), Math.Min(1, seconds * 3));
    }

    private static void CommitMove(AircraftWorld world, Aircraft plane, Vector3 next, IFlightEnvironment environment, bool hardLanding)
    {
        float height = environment.GroundHeight(next);
        plane.IsOnGround = next.Y <= height + 0.001f;
        if (!environment.IsInWorld(next) || environment.IsBlocked(plane.Position + Vector3.UnitY, next + Vector3.UnitY, AircraftRules.CollisionRadius)) { world.Explode(plane, environment); return; }
        plane.Position = new Vector3(next.X, Math.Max(height, next.Y), next.Z);
        if (next.Y < height && hardLanding) world.Explode(plane, environment);
        else if (next.Y < height) plane.Velocity = new Vector3(plane.Velocity.X, 0, plane.Velocity.Z);
    }

    private static Vector3 LimitSpeed(Vector3 velocity) => velocity.LengthSquared() > AircraftRules.MaximumSpeed * AircraftRules.MaximumSpeed ? Vector3.Normalize(velocity) * AircraftRules.MaximumSpeed : velocity;
    private static float Approach(float value, float target, float step) => value + Math.Max(-step, Math.Min(step, target - value));
}
