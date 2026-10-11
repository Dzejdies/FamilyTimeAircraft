using System;
using System.Collections.Generic;
using System.Reflection;
using GAT;
using HarmonyLib;

namespace FamilyTimeAircraft.Diagnostics;

[HarmonyPatch]
public static class AircraftNativeClickProbe
{
    public static int ObservedCallbacks { get; private set; }
    public static int ExecutedCallbacks { get; private set; }

    private static bool Prepare() => Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-controls-smoke") >= 0
        || Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-scenario") >= 0;

    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string name in new[] { "OnToolPrimaryStarted", "OnToolPrimaryCanceled", "OnToolSecondaryStarted", "OnToolSecondaryCanceled" })
            yield return AccessTools.Method(typeof(PlayerController), name);
    }

    private static void Postfix(bool __runOriginal)
    {
        ObservedCallbacks++;
        if (__runOriginal) ExecutedCallbacks++;
    }
}
