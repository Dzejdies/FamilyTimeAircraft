using System.Reflection;
using GAT;
using HarmonyLib;
using UnityEngine;

namespace FamilyTimeAircraft;

[HarmonyPatch(typeof(FollowController), nameof(FollowController.OnFollowInputCanceled))]
internal static class AircraftNativeGoToPatch
{
    private static readonly FieldInfo LongPressTriggered = AccessTools.Field(typeof(FollowController), "_followLongPressTriggered")
        ?? throw new System.MissingFieldException("Native follow long-press state is unavailable.");
    private static readonly FieldInfo Pickup = AccessTools.Field(typeof(FollowController), "_pickup")
        ?? throw new System.MissingFieldException("Native follow pickup state is unavailable.");

    private static void Prefix(FollowController __instance, bool __runOriginal, out Vector3? __state)
    {
        __state = null;
        if (!__runOriginal || AircraftRuntime.Current?.IsAuthoritative != true || !__instance.ShowFollowCursorHud) return;
        if (!(bool)LongPressTriggered.GetValue(__instance)) return;
        if (((PickupWorkerScheduler)Pickup.GetValue(__instance)).SelectedCount != 0) return;
        __state = __instance.FollowCursorTargetPosition;
    }

    private static void Postfix(Vector3? __state, bool __runOriginal)
    {
        if (!__runOriginal || !__state.HasValue) return;
        AircraftRuntime.Current?.OrderAircraftChildrenToGoTo(GameFlightEnvironment.ToCore(__state.Value));
    }
}
