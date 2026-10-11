using System;
using System.Collections.Generic;
using System.Numerics;

namespace FamilyTimeAircraft.Core;

public enum FlightPhase { Parked, Taxiing, TakingOff, Cruising, Approaching, Landing, Manual, CrashApproach, Wrecked, EmergencyLanding, Unpiloted }
public enum OccupantKind : byte { Child, Player }
public enum AircraftEventKind : byte { Explosion, OccupantLanded, GroundGoTo }
public enum AircraftCommandKind : byte { BoardPlayer, BoardChildren, AssignChildPilot, Disembark, FlightToAirfield, CrashAt, ManualControl, Repair, ExitPlayer, ParachuteDrop }

public interface IFlightEnvironment
{
    bool IsInWorld(Vector3 position);
    float GroundHeight(Vector3 position);
    bool IsBlocked(Vector3 from, Vector3 to, float radius);
    bool IsRunwayClear(Airfield airfield);
}

public sealed class Occupant
{
    public Occupant(OccupantKind kind, int id, string owner)
    {
        if (!Enum.IsDefined(typeof(OccupantKind), kind) || id < 0 || kind == OccupantKind.Child && id >= ushort.MaxValue)
            throw new ArgumentException("Invalid occupant identity.");
        Kind = kind; Id = id; Owner = AircraftRules.RequireOwner(owner);
    }
    public OccupantKind Kind { get; }
    public int Id { get; }
    public string Owner { get; }
    public string Key => $"{(byte)Kind}:{Id}";
}

public readonly struct FlightInput
{
    public FlightInput(float throttle, float pitch, float yaw, float roll, bool brake)
    {
        if (!AircraftRules.IsFinite(throttle) || throttle < 0 || throttle > 1 || !ValidAxis(pitch) || !ValidAxis(yaw) || !ValidAxis(roll))
            throw new ArgumentException("Flight controls must be finite and within their allowed range.");
        Throttle = throttle; Pitch = pitch; Yaw = yaw; Roll = roll; Brake = brake;
    }
    public float Throttle { get; }
    public float Pitch { get; }
    public float Yaw { get; }
    public float Roll { get; }
    public bool Brake { get; }
    private static bool ValidAxis(float value) => AircraftRules.IsFinite(value) && Math.Abs(value) <= 1;
}

public sealed class AircraftCommand
{
    public AircraftCommand(AircraftCommandKind kind, int aircraftId, int targetId = 0, Vector3 position = default, FlightInput input = default)
    {
        if (!Enum.IsDefined(typeof(AircraftCommandKind), kind) || aircraftId < 1 || !AircraftRules.IsFinite(position))
            throw new ArgumentException("Invalid aircraft command.");
        if ((kind == AircraftCommandKind.ExitPlayer || kind == AircraftCommandKind.ParachuteDrop) && targetId != 0)
            throw new ArgumentException("This aircraft command resolves passengers through the authenticated issuer.");
        Kind = kind; AircraftId = aircraftId; TargetId = targetId; Position = position; Input = input;
    }
    public AircraftCommandKind Kind { get; }
    public int AircraftId { get; }
    public int TargetId { get; }
    public Vector3 Position { get; }
    public FlightInput Input { get; }
}

public sealed class AircraftEvent
{
    internal AircraftEvent(AircraftEventKind kind, long sequence, int aircraftId, Vector3 position, IReadOnlyList<Occupant> occupants)
    {
        Kind = kind; Sequence = sequence; AircraftId = aircraftId; Position = position; Occupants = occupants;
    }
    public AircraftEventKind Kind { get; }
    public long Sequence { get; }
    public int AircraftId { get; }
    public Vector3 Position { get; }
    public IReadOnlyList<Occupant> Occupants { get; }
}

public sealed class EjectedOccupant
{
    internal EjectedOccupant(Occupant occupant, int aircraftId, Vector3 position, Vector3 velocity)
    {
        Occupant = occupant; AircraftId = aircraftId; Position = position; Velocity = velocity;
    }
    public Occupant Occupant { get; }
    public int AircraftId { get; }
    public Vector3 Position { get; internal set; }
    public Vector3 Velocity { get; internal set; }
    public float Elapsed { get; internal set; }
    public Vector3? ParachuteTarget { get; internal set; }
    public bool HasParachute => ParachuteTarget.HasValue;
}

public static class AircraftRules
{
    public const string SharedOwner = "shared";
    public const int AutomaticBoardingSeat = -2;
    public const int PassengerCapacity = 8;
    public const int MaximumAircraft = 128;
    public const int MaximumAirfields = 128;
    public const float MaximumSpeed = 36;
    public const float CruiseSpeed = 24;
    public const float CruiseAltitude = 35;
    public const float FixedStep = 1f / 60;
    public const float Gravity = 9.81f;
    public const float EjectionTimeout = 8;
    public const float CollisionRadius = 1.2f;
    public const float ParachuteMaximumElapsed = 120;
    public const float ParachuteSinkSpeed = 5;
    public const float OccupantReleaseHeight = 2;
    public const float OccupantCollisionRadius = 0.3f;
    public const float ParachuteMaximumHorizontalSpeed = 12;
    public const float ParachuteMaximumRange = 500;
    public const float ParachuteGlideRatio = 2;
    public const float ParachuteMinimumHeight = 12;
    public const float ParachuteDropInterval = 0.5f;
    public const float ParachuteLandingSpread = 2.5f;
    public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public static bool IsFinite(Vector3 value) => IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);
    public static string RequireOwner(string owner) => !string.IsNullOrWhiteSpace(owner) && owner.Length <= 512 ? owner : throw new ArgumentException("Invalid aircraft owner.");
    public static void RequirePosition(Vector3 position)
    {
        if (!IsFinite(position)) throw new ArgumentException("Aircraft position must be finite.");
    }
    public static void RequireRotation(Quaternion rotation)
    {
        if (!IsFinite(rotation.X) || !IsFinite(rotation.Y) || !IsFinite(rotation.Z) || !IsFinite(rotation.W) || Math.Abs(rotation.LengthSquared() - 1) > 0.01f)
            throw new ArgumentException("Aircraft rotation must be a unit quaternion.");
    }
    public static bool CanControlAircraft(Aircraft aircraft, string issuerOwner) =>
        aircraft.Owner == SharedOwner || aircraft.Owner == RequireOwner(issuerOwner);

    public static int ResolveAutomaticBoardingSeat(Aircraft aircraft, string issuerOwner, bool pilotReserved = false) =>
        !pilotReserved && !aircraft.Seats.ContainsKey(0) && CanControlAircraft(aircraft, issuerOwner) ? 0 : -1;

    public static void RequireOwner(Aircraft aircraft, string issuerOwner)
    {
        if (!CanControlAircraft(aircraft, issuerOwner)) throw new InvalidOperationException("This aircraft belongs to another family.");
    }
}
