using System;
using System.Reflection;
using FamilyTimeMultiplayer.Commands;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeAircraft.Multiplayer;

[HarmonyPatch(typeof(FollowController), nameof(FollowController.OnFollowInputCanceled))]
public static class AircraftRemoteCursorProbe
{
    public static int Releases { get; private set; }
    public static bool CursorValid { get; private set; }
    public static Vector3 Target { get; private set; }

    private static bool Prepare() => Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-remote-cursor-smoke") >= 0;

    [HarmonyPriority(Priority.First)]
    private static void Prefix(FollowController __instance)
    {
        if (CommanderContext.Active is not IRemoteCommanderIdentity commander) return;
        Releases++;
        CursorValid = __instance.ShowFollowCursorHud;
        Target = __instance.FollowCursorTargetPosition;
        float range = (float)AccessTools.Field(typeof(FollowController), "_followCursorMaxDistance").GetValue(__instance);
        bool nativeHit = World.Raycast(commander.Eye.position, commander.Eye.forward, 40, out World.HitInfo shortHit, CPUCollisionMask.DEFAULT);
        bool extendedHit = World.Raycast(commander.Eye.position, commander.Eye.forward, 1000, out World.HitInfo longHit, CPUCollisionMask.DEFAULT);
        AircraftRuntime.Current?.SetNotice("[AircraftRemoteCursorEvidence] player=" + commander.PlayerId + " eye=" + commander.Eye.position
            + " direction=" + commander.Eye.forward + " cursorValid=" + CursorValid + " target=" + Target + " restoredRange=" + range
            + " native40Hit=" + nativeHit + " extendedHit=" + extendedHit + " actualHit=" + longHit.point + " distance=" + longHit.distance
            + " walkable=" + (extendedHit && World.navManager.IsWalkableSurface(Vector3Int.FloorToInt(longHit.point))));
    }
}

[HarmonyPatch]
public static class AircraftRemoteCursorResolveProbe
{
    public static bool BeyondNativeRange { get; private set; }
    private static bool Prepare() => Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-remote-cursor-smoke") >= 0;
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(FollowController), "ResolveFollowCursorTarget")
        ?? AccessTools.Method(typeof(FollowController), "UpdateFollowCursorTarget");

    [HarmonyPriority(Priority.First)]
    [HarmonyBefore("dzejdies.familytime.multiplayer")]
    private static void Prefix(FollowController __instance)
    {
        if (BeyondNativeRange || CommanderContext.Active is not IRemoteCommanderIdentity commander) return;
        bool shortHit = World.Raycast(commander.Eye.position, commander.Eye.forward, 40, out World.HitInfo ignored, CPUCollisionMask.DEFAULT);
        bool longHit = World.Raycast(commander.Eye.position, commander.Eye.forward, 1000, out World.HitInfo actual, CPUCollisionMask.DEFAULT);
        if (shortHit || !longHit || !World.navManager.IsWalkableSurface(Vector3Int.FloorToInt(actual.point))) return;
        BeyondNativeRange = true;
        float range = (float)AccessTools.Field(typeof(FollowController), "_followCursorMaxDistance").GetValue(__instance);
        AircraftRuntime.Current?.SetNotice("[AircraftRemoteCursorResolveEvidence] eye=" + commander.Eye.position + " direction=" + commander.Eye.forward
            + " actualRange=" + range + " native40Hit=False extendedHit=True walkable=True target=" + actual.point + " distance=" + actual.distance);
    }
}
