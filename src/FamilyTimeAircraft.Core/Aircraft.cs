using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;

namespace FamilyTimeAircraft.Core;

public sealed class Aircraft
{
    internal readonly Dictionary<int, Occupant> Cabin = new Dictionary<int, Occupant>();
    internal FlightInput Input;
    internal int? OriginAirfieldId;
    internal Vector3? CrashTarget;
    internal Vector3? EmergencyLandingPoint;
    internal float Pitch;
    internal float Roll;
    internal float Yaw;
    internal bool PilotReturnPending;
    internal string? PilotGroundOwner;
    public Vector3? PilotGroundTarget { get; internal set; }
    internal Aircraft(int id, string owner, Vector3 position, Quaternion rotation)
    {
        Id = id; Owner = AircraftRules.RequireOwner(owner);
        AircraftRules.RequirePosition(position); AircraftRules.RequireRotation(rotation);
        Position = position; Rotation = rotation;
        Yaw = MathF.Atan2(Vector3.Transform(Vector3.UnitZ, rotation).X, Vector3.Transform(Vector3.UnitZ, rotation).Z);
        Seats = new ReadOnlyDictionary<int, Occupant>(Cabin);
    }
    public int Id { get; }
    public string Owner { get; }
    public Vector3 Position { get; internal set; }
    public Quaternion Rotation { get; internal set; }
    public Vector3 Velocity { get; internal set; }
    public bool IsOnGround { get; internal set; }
    public FlightPhase Phase { get; internal set; }
    public int? TargetAirfieldId { get; internal set; }
    public IReadOnlyDictionary<int, Occupant> Seats { get; }
    public FlightInput CurrentInput => Input;
    public bool CanBoard => Phase == FlightPhase.Parked || Phase == FlightPhase.Manual && IsOnGround && Velocity.Length() < 0.5f;
    public Vector3 SeatPosition(int seatIndex)
    {
        if (seatIndex < 0 || seatIndex > AircraftRules.PassengerCapacity) throw new ArgumentOutOfRangeException(nameof(seatIndex));
        Vector3 local = seatIndex == 0 ? new Vector3(0, 1.6f, 1.4f) : new Vector3(seatIndex % 2 == 1 ? -0.5f : 0.5f, 1.43f, 0.45f - (seatIndex - 1) / 2 * 0.78f);
        return Position + Vector3.Transform(local, Rotation);
    }
}

public sealed class Airfield
{
    public const float Length = 80;
    public const float Width = 16;
    public const float MaximumTerrainDeviation = 0.35f;
    internal Airfield(int id, string owner, Vector3 position, Quaternion rotation)
    {
        Id = id; Owner = AircraftRules.RequireOwner(owner);
        AircraftRules.RequirePosition(position); AircraftRules.RequireRotation(rotation);
        Position = position; Rotation = rotation;
    }
    public int Id { get; }
    public string Owner { get; }
    public Vector3 Position { get; }
    public Quaternion Rotation { get; }
    public int? ReservedBy { get; internal set; }
    public IEnumerable<Vector3> TerrainSamples() => TerrainSamples(Position, Rotation);
    public static IEnumerable<Vector3> TerrainSamples(Vector3 position, Quaternion rotation)
    {
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, rotation);
        Vector3 side = Vector3.Transform(Vector3.UnitX, rotation);
        for (int length = -(int)Length / 2; length <= (int)Length / 2; length++)
            for (int width = -(int)Width / 2; width <= (int)Width / 2; width++)
                yield return position + forward * length + side * width;
    }

    public Vector3 Forward => Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, Rotation));
    public Vector3 Entry => Position - Forward * (Length * 0.4f);
    public Vector3 Exit => Position + Forward * (Length * 0.4f);
}
