using System;
using FamilyTimeMultiplayer.Commands;
using FamilyTimeMultiplayer.Core.Protocol;
using HarmonyLib;

namespace FamilyTimeAircraft.Multiplayer;

[HarmonyPatch(typeof(ClientCommandBridge), nameof(ClientCommandBridge.Send))]
public static class AircraftNativeInputProbe
{
    public static int FollowRequests { get; private set; }

    private static bool Prepare() => Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-scenario") >= 0;

    private static void Prefix(CommandIntentMessage intent)
    {
        if (intent.Kind == CommandIntentKind.FollowPressed || intent.Kind == CommandIntentKind.FollowReleased) FollowRequests++;
    }
}
