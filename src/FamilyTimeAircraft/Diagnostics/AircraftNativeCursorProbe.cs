using System;
using System.Reflection;
using GAT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FamilyTimeAircraft.Diagnostics;

[HarmonyPatch]
public static class AircraftNativeCursorProbe
{
    public static float LastRange { get; private set; }

    private static bool Prepare() => Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-parachute-smoke") >= 0
        || Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-scenario") >= 0;

    private static MethodBase TargetMethod() => AccessTools.Method(typeof(FollowController), "ResolveFollowCursorTarget")
        ?? AccessTools.Method(typeof(FollowController), "UpdateFollowCursorTarget");

    [HarmonyPriority(Priority.Last)]
    private static void Prefix(FollowController __instance)
    {
        LastRange = (float)AccessTools.Field(typeof(FollowController), "_followCursorMaxDistance").GetValue(__instance);
        var camera = (Camera)AccessTools.Field(typeof(FollowController), "_camera").GetValue(__instance);
        Vector2 pointer = Mouse.current?.position.ReadValue() ?? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Ray ray = camera.ScreenPointToRay(pointer);
        bool hit = World.Raycast(ray.origin, ray.direction, LastRange, out World.HitInfo info, CPUCollisionMask.DEFAULT);
        bool walkable = hit && World.navManager.IsWalkableSurface(Vector3Int.FloorToInt(info.point));
        AircraftRuntime.Current?.SetNotice("[AircraftNativeCursorEvidence] range=" + LastRange + " camera=" + camera.transform.position
            + " rotation=" + camera.transform.eulerAngles + " pointer=" + pointer + " ray=" + ray + " hit=" + hit
            + " point=" + info.point + " distance=" + info.distance + " walkable=" + walkable);
    }
}
