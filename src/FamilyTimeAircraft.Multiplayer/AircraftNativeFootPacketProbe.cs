using System;
using FamilyTimeMultiplayer.Core.Protocol;
using FamilyTimeMultiplayer.Core.Transport;
using HarmonyLib;

namespace FamilyTimeAircraft.Multiplayer;

[HarmonyPatch(typeof(SignedClientConnection), nameof(SignedClientConnection.Send))]
public static class AircraftNativeFootPacketProbe
{
    public static int FootRequests { get; private set; }

    public static int NonFollowRequests { get; private set; }
    public static float LastViewPitch { get; private set; }
    public static float LastViewYaw { get; private set; }

    private static bool Prepare() => Array.IndexOf(Environment.GetCommandLineArgs(), "-fta-scenario") >= 0;

    private static void Prefix(INetMessage message)
    {
        if (message is ClientPoseMessage view) { LastViewPitch = view.PitchDegrees; LastViewYaw = view.YawDegrees; }
        if (message is CommandIntentMessage intent)
        {
            if (intent.Kind != CommandIntentKind.FollowPressed && intent.Kind != CommandIntentKind.FollowReleased) NonFollowRequests++;
        }
        else if (message is HoldMessage || message is ToolEditsMessage || message is MamaToggleMessage || message is BedRequestMessage || message is WorldEditMessage) NonFollowRequests++;
        if (message is CommandIntentMessage || message is HoldMessage || message is ToolEditsMessage
            || message is MamaToggleMessage || message is BedRequestMessage || message is WorldEditMessage) FootRequests++;
    }
}
