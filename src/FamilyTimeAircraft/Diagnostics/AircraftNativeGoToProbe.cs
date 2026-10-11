using System;
using GAT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FamilyTimeAircraft.Diagnostics;

[HarmonyPatch(typeof(FollowController), nameof(FollowController.OnFollowInputCanceled))]
public static class AircraftNativeGoToProbe
{
    public static int CursorReleases { get; private set; }
    public static Vector3? LastCursorTarget { get; private set; }

    private static bool Prepare() => Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-parachute-smoke") >= 0
        || Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-scenario") >= 0;

    private static void Prefix(FollowController __instance, bool __runOriginal)
    {
        AircraftRuntime.Current?.SetNotice("[AircraftNativeGoToEvidence] original=" + __runOriginal + " following=" + __instance.IsFollowing
            + " pressed=" + AccessTools.Field(typeof(FollowController), "_followPressed").GetValue(__instance)
            + " longpress=" + AccessTools.Field(typeof(FollowController), "_followLongPressTriggered").GetValue(__instance)
            + " cursorActive=" + AccessTools.Field(typeof(FollowController), "_followCursorTargetActive").GetValue(__instance)
            + " cursorValid=" + __instance.ShowFollowCursorHud + " point=" + __instance.FollowCursorTargetPosition
            + " maxDistance=" + AccessTools.Field(typeof(FollowController), "_followCursorMaxDistance").GetValue(__instance)
            + " mouse=" + Mouse.current?.position.ReadValue() + " camera=" + Camera.main?.transform.position);
        if (!__runOriginal || !__instance.ShowFollowCursorHud) return;
        CursorReleases++;
        LastCursorTarget = __instance.FollowCursorTargetPosition;
    }
}
