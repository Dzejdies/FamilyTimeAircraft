using System.Collections.Generic;
using System.Reflection;
using FamilyTimeAircraft.Core;
using GAT;
using HarmonyLib;
using UnityEngine;
using NVector = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace FamilyTimeAircraft;

public sealed class GameFlightEnvironment : IFlightEnvironment
{
    private const float WorldExtent = 4096;
    private const float MaximumAltitude = 512;
    internal const float MinimumAltitude = -16;
    private const float RunwayCheckInterval = 0.25f;
    private const float AircraftProbeOriginHeight = 1;
    public AircraftImpactDamage ImpactDamage { get; } = new();
    public string TowerPlacementFailure { get; private set; } = string.Empty;
    private static readonly MethodInfo? WaterNavigationCheck = AccessTools.Method(typeof(NavManager), "IsWaterNavigationCell", new[] { typeof(Vector3Int) });
    private readonly Dictionary<int, (Airfield Field, float Expires, bool Clear)> _runwayChecks = new();

    public bool IsInWorld(NVector position) => position.X >= -WorldExtent * 0.5f && position.Z >= -WorldExtent * 0.5f
        && position.X < WorldExtent * 0.5f && position.Z < WorldExtent * 0.5f && position.Y >= MinimumAltitude && position.Y < MaximumAltitude;

    public float GroundHeight(NVector position) => World.heightfield != null
        ? World.heightfield.GetWorldHeight(position.X, position.Z) : 0;

    public bool IsBlocked(NVector from, NVector to, float radius)
    {
        Vector3 start = ToUnity(from);
        if (radius >= 1) start -= Vector3.up * AircraftProbeOriginHeight;
        Vector3 displacement = ToUnity(to - from);
        if (displacement.sqrMagnitude < 0.0001f) return false;
        Vector3 direction = displacement.normalized;
        Vector3 horizontalForward = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (horizontalForward.sqrMagnitude < 0.0001f) horizontalForward = Vector3.forward;
        Vector3 side = Vector3.Cross(horizontalForward, Vector3.up).normalized;
        Vector3[] offsets = radius >= 1
            ? new[] { Vector3.up * 0.3f, Vector3.up * 1.6f + horizontalForward * 4, Vector3.up * 1.6f - horizontalForward * 4,
                Vector3.up * 2 + side * 5.5f, Vector3.up * 2 - side * 5.5f, Vector3.up * 3.3f }
            : new[] { Vector3.zero, side * radius, -side * radius, Vector3.up * radius };
        bool blocked = false;
        foreach (Vector3 offset in offsets)
        {
            if (!World.Raycast(start + offset, direction, displacement.magnitude, out var hit, CPUCollisionMask.DEFAULT)) continue;
            blocked = true;
            if (radius >= 1 && AircraftRuntime.Current?.IsAuthoritative == true)
            {
                float speed = Mathf.Min(TowerRules.MaximumImpactSpeed, displacement.magnitude / Time.fixedDeltaTime);
                ImpactDamage.Record(from - new NVector(0, AircraftProbeOriginHeight, 0), hit,
                    new NVector(direction.x, direction.y, direction.z), speed);
            }
        }
        return blocked;
    }

    public bool IsRunwayClear(Airfield airfield)
    {
        float now = Time.realtimeSinceStartup;
        if (_runwayChecks.TryGetValue(airfield.Id, out var cached) && cached.Field == airfield && now < cached.Expires) return cached.Clear;
        bool clear = CanPlaceRunway(airfield.Position, airfield.Rotation);
        if (_runwayChecks.Count >= AircraftRules.MaximumAirfields) _runwayChecks.Clear();
        _runwayChecks[airfield.Id] = (airfield, now + RunwayCheckInterval, clear);
        return clear;
    }

    public bool CanPlaceRunway(NVector position, NQuaternion rotation)
    {
        foreach (NVector point in Airfield.TerrainSamples(position, rotation))
            if (!CheckRunwayPoint(point, position.Y)) return false;
        return true;
    }

    public bool CanPlaceTowerPair(NVector position, NQuaternion rotation)
    {
        TowerPlacementFailure = string.Empty;
        foreach (NVector point in TowerPlacementSamples(position, rotation))
            if (!CheckTowerPoint(point, position.Y, out string failure))
            {
                TowerPlacementFailure = failure;
                return false;
            }
        return true;
    }

    private static IEnumerable<NVector> TowerPlacementSamples(NVector position, NQuaternion rotation)
    {
        int[] xOffsets = { -TowerLayout.TowerFootprintHalfWidth, 0, TowerLayout.TowerFootprintHalfWidth };
        int[] zOffsets = { -TowerLayout.TowerFootprintHalfDepth, 0, TowerLayout.TowerFootprintHalfDepth };
        foreach (int x in xOffsets)
            foreach (int z in zOffsets)
            {
                NVector local = new(x, 0, z);
                NVector rotated = NVector.Transform(local, rotation);
                yield return position + rotated;
            }
    }

    private bool CheckTowerPoint(NVector point, float elevation, out string failure)
    {
        if (!IsInWorld(point)) { failure = "world bounds"; return false; }
        if (System.Math.Abs(GroundHeight(point) - elevation) > Airfield.MaximumTerrainDeviation) { failure = "uneven terrain"; return false; }
        if (World.navManager != null && WaterNavigationCheck != null &&
            (bool)WaterNavigationCheck.Invoke(World.navManager, new object[] { Vector3Int.RoundToInt(ToUnity(point)) })) { failure = "water"; return false; }
        NVector from = point + NVector.UnitY * 0.4f;
        bool blocked = IsBlocked(from, from + NVector.UnitY * TowerLayout.MaximumTowerHeight, 0.3f);
        failure = blocked ? "obstruction" : string.Empty;
        return !blocked;
    }

    private bool CheckRunwayPoint(NVector point, float elevation)
    {
        if (!IsInWorld(point) || System.Math.Abs(GroundHeight(point) - elevation) > Airfield.MaximumTerrainDeviation) return false;
        if (World.navManager != null && WaterNavigationCheck != null &&
            (bool)WaterNavigationCheck.Invoke(World.navManager, new object[] { Vector3Int.RoundToInt(ToUnity(point)) })) return false;
        NVector from = point + NVector.UnitY * 0.4f;
        return !IsBlocked(from, from + NVector.UnitY * 3, 0.3f);
    }

    public static Vector3 ToUnity(NVector value) => new(value.X, value.Y, value.Z);
    public static NVector ToCore(Vector3 value) => new(value.x, value.y, value.z);
    public static Quaternion ToUnity(NQuaternion value) => new(value.X, value.Y, value.Z, value.W);
    public static NQuaternion ToCore(Quaternion value) => new(value.x, value.y, value.z, value.w);
}
