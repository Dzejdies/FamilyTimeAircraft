using System;
using GAT;
using HarmonyLib;
using UnityEngine;
using NVector = System.Numerics.Vector3;

namespace FamilyTimeAircraft.Diagnostics;

[HarmonyPatch(typeof(AircraftImpactDamage), nameof(AircraftImpactDamage.Record))]
internal static class AircraftImpactContactProbe
{
    private static bool Prepare() => Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-impact-smoke") >= 0;

    private static void Prefix(NVector __0, World.HitInfo __1)
    {
        bool part = World.buildPartManager.TryGetBuildPart(__1, out int template, out Vector3Int position, out BuildPart.Rotation rotation);
        AircraftRuntime.Current?.SetNotice("[AircraftImpactContactEvidence] Record origin=" + __0 + " hit=" + __1.point
            + " template=" + template + " grid=" + position + " resolves=" + part + " fixed=" + Time.fixedTime + " frame=" + Time.frameCount);
    }
}

[HarmonyPatch(typeof(AircraftImpactDamage), nameof(AircraftImpactDamage.Apply))]
internal static class AircraftImpactApplyProbe
{
    private static bool Prepare() => Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-impact-smoke") >= 0;

    private static void Postfix(NVector __0, int __result)
    {
        AircraftRuntime.Current?.SetNotice("[AircraftImpactContactEvidence] Apply origin=" + __0 + " removed=" + __result
            + " fixed=" + Time.fixedTime + " frame=" + Time.frameCount);
    }
}
