using System;
using System.Collections.Generic;
using System.Reflection;
using FamilyTimeAircraft.Core;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeAircraft;

[HarmonyPatch]
internal static class AircraftCursorRangePatch
{
    private static readonly FieldInfo MaximumDistance = RequireField("_followCursorMaxDistance", typeof(float));
    private static readonly FieldInfo FollowCamera = RequireField("_camera", typeof(Camera));

    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string name in new[] { "ResolveFollowCursorTarget", "UpdateFollowCursorTarget" })
        {
            MethodInfo? method = AccessTools.Method(typeof(FollowController), name, Type.EmptyTypes);
            if (method == null) continue;
            yield return method;
            yield break;
        }
        throw new MissingMethodException("Native follow cursor targeting is unavailable.");
    }

    [HarmonyPriority(Priority.First)]
    [HarmonyBefore("dzejdies.familytime.multiplayer")]
    private static void Prefix(FollowController __instance, out float? __state)
    {
        __state = null;
        AircraftRuntime? runtime = AircraftRuntime.Current;
        if (runtime == null || !AircraftInputState.IsPlayerPassenger(runtime.CommandPlayerId?.Invoke() ?? runtime.LocalPlayerId)) return;
        if (FollowCamera.GetValue(__instance) is not Camera camera) return;
        __state = (float)MaximumDistance.GetValue(__instance);
        float aircraftRange = Math.Max(0, camera.transform.position.y - GameFlightEnvironment.MinimumAltitude) + AircraftRules.ParachuteMaximumRange;
        MaximumDistance.SetValue(__instance, Math.Max(__state.Value, aircraftRange));
    }

    private static Exception? Finalizer(FollowController __instance, float? __state, Exception? __exception)
    {
        if (__state.HasValue) MaximumDistance.SetValue(__instance, __state.Value);
        return __exception;
    }

    private static FieldInfo RequireField(string name, Type expectedType)
    {
        FieldInfo field = AccessTools.Field(typeof(FollowController), name)
            ?? throw new MissingFieldException($"Native follow field {name} is unavailable.");
        if (field.FieldType != expectedType) throw new InvalidOperationException($"Native follow field {name} has changed.");
        return field;
    }
}
