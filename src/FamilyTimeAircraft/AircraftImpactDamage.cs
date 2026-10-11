using System;
using System.Collections.Generic;
using FamilyTimeAircraft.Core;
using GAT;
using UnityEngine;
using NativeWorld = GAT.World;
using FlightVector = System.Numerics.Vector3;

namespace FamilyTimeAircraft;

public sealed class AircraftImpactDamage
{
    private const int MaximumContactsPerAircraft = 6;
    private const int MaximumRecordedHits = AircraftRules.MaximumAircraft * MaximumContactsPerAircraft;
    private const float OriginToleranceSquared = 0.0001f;

    private readonly struct PartKey : IEquatable<PartKey>
    {
        public readonly int Template;
        public readonly Vector3Int Position;
        public readonly BuildPart.Rotation Rotation;
        public PartKey(int template, Vector3Int position, BuildPart.Rotation rotation)
        { Template = template; Position = position; Rotation = rotation; }
        public bool Equals(PartKey other) => Template == other.Template && Position == other.Position && Rotation == other.Rotation;
        public override bool Equals(object? value) => value is PartKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Template, Position, Rotation);
    }

    private readonly struct Contact
    {
        public readonly FlightVector Origin;
        public readonly NativeWorld.HitInfo Hit;
        public readonly PartKey Key;
        public readonly float Speed;
        public readonly FlightVector Direction;
        public readonly long ImpactSequence;
        public readonly float FixedTime;
        public readonly int Frame;
        public Contact(FlightVector origin, NativeWorld.HitInfo hit, PartKey key, float speed, FlightVector direction, long impactSequence)
        { Origin = origin; Hit = hit; Key = key; Speed = speed; Direction = direction; ImpactSequence = impactSequence; FixedTime = Time.fixedTime; Frame = Time.frameCount; }
    }

    private readonly List<Contact> _hits = new();
    private NativeWorld? _world;
    private BuildPartManager? _manager;
    private long _nextImpactSequence;
    private long _currentImpactSequence;
    private FlightVector _currentImpactOrigin;
    private float _currentImpactTime;
    private int _currentImpactFrame;

    public void Record(FlightVector aircraftOrigin, NativeWorld.HitInfo hit, FlightVector direction, float speed)
    {
        if (AircraftRuntime.Current?.IsAuthoritative != true || !IsFinite(aircraftOrigin) || !IsFinite(direction) || direction.LengthSquared() < 0.0001f) return;
        CaptureNativeWorld();
        _hits.RemoveAll(contact => !IsCurrentStep(contact));
        if (_world == null || _manager == null || !TryResolve(hit, out PartKey key)) return;
        long impactSequence = ResolveImpactSequence(aircraftOrigin);
        foreach (Contact contact in _hits)
            if (IsCurrentStep(contact) && SameOrigin(contact.Origin, aircraftOrigin) && contact.Key.Equals(key)) return;
        if (_hits.Count == MaximumRecordedHits) throw new InvalidOperationException("Aircraft impact contact limit exceeded for one simulation step.");
        _hits.Add(new Contact(aircraftOrigin, hit, key, speed, FlightVector.Normalize(direction), impactSequence));
    }

    private long ResolveImpactSequence(FlightVector origin)
    {
        if (_currentImpactSequence == 0 || _currentImpactTime != Time.fixedTime || _currentImpactFrame != Time.frameCount || !SameOrigin(_currentImpactOrigin, origin))
        {
            _currentImpactSequence = checked(++_nextImpactSequence);
            _currentImpactOrigin = origin;
            _currentImpactTime = Time.fixedTime;
            _currentImpactFrame = Time.frameCount;
        }
        return _currentImpactSequence;
    }

    private void CaptureNativeWorld()
    {
        if (ReferenceEquals(_world, NativeWorld.Instance) && ReferenceEquals(_manager, NativeWorld.buildPartManager)) return;
        Clear();
        _world = NativeWorld.Instance;
        _manager = NativeWorld.buildPartManager;
    }

    public int Apply(FlightVector origin)
    {
        if (!CanApply(origin)) { Clear(); return 0; }
        List<Contact> consumed = _hits.FindAll(contact => IsCurrentStep(contact) && SameOrigin(contact.Origin, origin));
        _hits.RemoveAll(contact => SameOrigin(contact.Origin, origin));
        int removed = 0;
        var applied = new HashSet<PartKey>();
        foreach (Contact contact in consumed)
            if (applied.Add(contact.Key) && TryResolve(contact.Hit, out PartKey current) && current.Equals(contact.Key))
            {
                if (TryApplyTowerImpact(contact)) continue;
                Remove(contact);
                removed++;
            }
        return removed;
    }

    private bool TryApplyTowerImpact(Contact contact)
    {
        AircraftRuntime runtime = AircraftRuntime.Current!;
        if (!runtime.Blueprints.IsTowerSectionTemplate(contact.Key.Template)) return false;
        if (!runtime.TryResolveTowerSection(contact.Key.Template, contact.Key.Position, contact.Key.Rotation, out TowerPair pair, out TowerSectionState section))
            throw new InvalidOperationException("Aircraft impact contacted an untracked tower section.");
        FlightVector normal = ToFlightVector(contact.Hit.normal);
        if (!IsFinite(normal) || normal.LengthSquared() < 0.0001f) return true;
        runtime.ApplyTowerImpact(pair.Id, section.Id, contact.Speed, contact.Direction, FlightVector.Normalize(normal), contact.ImpactSequence);
        return true;
    }

    private static FlightVector ToFlightVector(Vector3 value) => new(value.x, value.y, value.z);

    private bool CanApply(FlightVector origin) => AircraftRuntime.Current?.IsAuthoritative == true && IsFinite(origin)
        && _world != null && _manager != null && ReferenceEquals(_world, NativeWorld.Instance)
        && ReferenceEquals(_manager, NativeWorld.buildPartManager);

    private bool TryResolve(NativeWorld.HitInfo hit, out PartKey key)
    {
        key = default;
        if (_manager == null || !_manager.TryGetBuildPart(hit, out int template, out Vector3Int position, out BuildPart.Rotation rotation)) return false;
        key = new PartKey(template, position, rotation);
        return true;
    }

    private void Remove(Contact contact)
    {
        _manager!.RemoveBuildPart(contact.Key.Template, contact.Key.Position, contact.Key.Rotation);
        if (TryResolve(contact.Hit, out _)) throw new InvalidOperationException("Native aircraft impact did not remove the contacted building part.");
    }

    private static bool SameOrigin(FlightVector first, FlightVector second) => FlightVector.DistanceSquared(first, second) <= OriginToleranceSquared;
    private static bool IsCurrentStep(Contact contact) => contact.FixedTime == Time.fixedTime && contact.Frame == Time.frameCount;
    private static bool IsFinite(FlightVector value) => !float.IsNaN(value.X) && !float.IsInfinity(value.X)
        && !float.IsNaN(value.Y) && !float.IsInfinity(value.Y) && !float.IsNaN(value.Z) && !float.IsInfinity(value.Z);

    public void Clear()
    {
        _hits.Clear();
        _world = null;
        _manager = null;
        _currentImpactSequence = 0;
        _currentImpactOrigin = default;
        _currentImpactTime = 0;
        _currentImpactFrame = 0;
    }
}
