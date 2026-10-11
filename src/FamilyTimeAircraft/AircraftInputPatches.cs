using System.Collections.Generic;
using System.Reflection;
using FamilyTimeAircraft.Core;
using GAT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace FamilyTimeAircraft;

internal static class AircraftInputState
{
    internal const int BoardingInputPriority = Priority.First + 1;
    public static Aircraft? LocalAircraft()
    {
        AircraftRuntime? runtime = AircraftRuntime.Current;
        if (runtime == null) return null;
        foreach (Aircraft plane in runtime.World.Aircrafts)
            foreach (Occupant occupant in plane.Seats.Values)
                if (occupant.Kind == OccupantKind.Player && occupant.Id == runtime.LocalPlayerId) return plane;
        return null;
    }

    public static bool IsLocalPassenger()
    {
        AircraftRuntime? runtime = AircraftRuntime.Current;
        return runtime != null && IsPlayerPassenger(runtime.LocalPlayerId);
    }

    public static bool IsPlayerPassenger(int playerId)
    {
        AircraftRuntime? runtime = AircraftRuntime.Current;
        if (runtime == null) return false;
        foreach (Aircraft plane in runtime.World.Aircrafts)
            foreach (Occupant occupant in plane.Seats.Values)
                if (occupant.Kind == OccupantKind.Player && occupant.Id == playerId) return true;
        foreach (EjectedOccupant occupant in runtime.World.Ejections)
            if (occupant.Occupant.Kind == OccupantKind.Player && occupant.Occupant.Id == playerId) return true;
        return false;
    }

    public static bool SuppressTools => IsLocalPassenger() || AircraftRuntime.Current?.GetComponent<AircraftMenu>().CapturesNativeCommands == true;

}

[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.MovementDirectionXZ), MethodType.Getter)]
internal static class AircraftSuppressWalkingPatch
{
    private static void Postfix(ref Vector2 __result)
    {
        if (AircraftInputState.SuppressTools) __result = Vector2.zero;
    }
}

[HarmonyPatch]
internal static class AircraftSuppressToolsPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string name in new[] { "OnToolPrimaryStarted", "OnToolPrimaryCanceled", "OnToolSecondaryStarted", "OnToolSecondaryCanceled", "OnInteractStarted", "OnInteractCanceled", "TryDispatchJump", "OnAltToolPerformed", "OnToolAltStarted", "OnToolAltCanceled", "OnScrollToolPerformed" })
        {
            MethodInfo? method = AccessTools.Method(typeof(PlayerController), name);
            if (method != null) yield return method;
        }
    }

    [HarmonyPriority(Priority.First)]
    [HarmonyBefore("dzejdies.familytime.multiplayer")]
    private static bool Prefix() => !AircraftInputState.SuppressTools;
}

[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.OnDamage))]
internal static class AircraftProtectEjectedPlayerPatch
{
    private static bool Prefix() => !AircraftInputState.IsLocalPassenger();
}

[HarmonyPatch(typeof(GATUtilities), nameof(GATUtilities.ApplyHealthDelta))]
internal static class AircraftProtectEjectedChildPatch
{
    private static bool Prefix(Handle targetHandle, short delta, System.Action<GATUtilities.InfoDeltaResult>? onResult)
    {
        AircraftRuntime? runtime = AircraftRuntime.Current;
        if (delta >= 0 || runtime == null) return true;
        foreach (EjectedOccupant ejected in runtime.World.Ejections)
        {
            if (ejected.Occupant.Kind != OccupantKind.Child) continue;
            Handle child = runtime.ResolveChildHandle?.Invoke(ejected.Occupant.Id) ?? new Handle { stableIndex = checked((ushort)ejected.Occupant.Id) };
            if (child != targetHandle) continue;
            onResult?.Invoke(new GATUtilities.InfoDeltaResult(targetHandle, false, 0, 0, Vector3.zero));
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(HandService), "GrabHandle")]
internal static class AircraftRejectReservedChildGrabPatch
{
    private static bool Prefix(Handle handle)
    {
        AircraftRuntime? runtime = AircraftRuntime.Current;
        if (runtime == null || !runtime.IsNativeChildReserved(handle)) return true;
        runtime.SetNotice("This child is boarding, seated, or being ejected from an aircraft.");
        return false;
    }
}

[HarmonyPatch]
internal static class AircraftRejectImmediateChildGrabPatch
{
    private static bool Prepare() => typeof(HandService).GetMethod("GrabHandleImmediately", BindingFlags.Instance | BindingFlags.NonPublic) != null;
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(HandService), "GrabHandleImmediately");
    private static bool Prefix(Handle handle, ref bool __result)
    {
        if (AircraftRuntime.Current?.IsNativeChildReserved(handle) != true) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(PlayerController), "OnInteractStarted")]
internal static class AircraftBoardingInputPatch
{
    internal static bool ConsumedGesture { get; private set; }

    [HarmonyPriority(AircraftInputState.BoardingInputPriority)]
    [HarmonyBefore("dzejdies.familytime.multiplayer")]
    private static bool Prefix(InputAction.CallbackContext __0) => !ConsumeBoardGesture(__0);

    internal static bool ConsumeBoardGesture(InputAction.CallbackContext context)
    {
        AircraftRuntime? runtime = AircraftRuntime.Current;
        if (runtime == null || context.control is not KeyControl key || key.keyCode != runtime.Controls.Board.Value) return false;
        if (!key.isPressed) return ConsumedGesture;
        ConsumedGesture = runtime.GetComponent<AircraftMenu>().TryToggleAircraftSeat();
        ConsumedGesture |= AircraftInputState.SuppressTools;
        return ConsumedGesture;
    }

    internal static bool ReleaseGesture()
    {
        bool consumed = ConsumedGesture;
        ConsumedGesture = false;
        return consumed;
    }
}

[HarmonyPatch]
internal static class AircraftBoardingInputReleasePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string name in new[] { "OnInteractCanceled", "OnToolAltCanceled" })
        {
            MethodInfo? method = typeof(PlayerController).GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method != null) yield return method;
        }
    }
    [HarmonyPriority(Priority.First)]
    [HarmonyBefore("dzejdies.familytime.multiplayer")]
    private static bool Prefix(InputAction.CallbackContext __0)
    {
        AircraftRuntime? runtime = AircraftRuntime.Current;
        bool boardKey = __0.control is KeyControl key && runtime != null && key.keyCode == runtime.Controls.Board.Value;
        return !(boardKey && AircraftBoardingInputPatch.ReleaseGesture()) && !AircraftInputState.SuppressTools;
    }
}

[HarmonyPatch]
internal static class AircraftSuppressCommandsPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string name in new[] { "OnInteract", "OnLongPressInteract", "OnCommandLockClick", "OnLongPressSecondary", "OnLongPressToolAlt" })
        {
            MethodInfo? method = AccessTools.Method(typeof(CommandService), name);
            if (method != null) yield return method;
        }
    }

    [HarmonyPriority(Priority.First)]
    [HarmonyBefore("dzejdies.familytime.multiplayer")]
    private static bool Prefix() => !AircraftInputState.SuppressTools;
}

[HarmonyPatch]
internal static class AircraftBoardingAltInputPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string name in new[] { "OnAltToolPerformed", "OnToolAltStarted" })
        {
            MethodInfo? method = typeof(PlayerController).GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method != null) yield return method;
        }
    }
    [HarmonyPriority(AircraftInputState.BoardingInputPriority)]
    [HarmonyBefore("dzejdies.familytime.multiplayer")]
    private static bool Prefix(InputAction.CallbackContext __0) => !AircraftBoardingInputPatch.ConsumeBoardGesture(__0);
}
